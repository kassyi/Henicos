using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Checks the conditions under which the oracle twin of a split method (<see cref="FuzzSplitOracle"/>) is the
/// definition of its result: every shuffle reads the calling lane with a full mask, every split point is reached by
/// all threads alike, and no jump lets threads leave a loop with a split point.
/// </summary>
internal static class FuzzSplitOracleGuard
{
    private static readonly string s_selfLaneNormalized =
        FuzzSplitOracle.SelfLaneExpression.Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>Returns why the twin of <paramref name="method"/> would be wrong, or <see langword="null"/>.</summary>
    internal static string? Validate(MethodDeclarationSyntax method, IReadOnlySet<string> splitMethodNames)
    {
        foreach (var node in method.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation && IsSplitPoint(invocation, splitMethodNames))
            {
                if (FuzzSplitOracle.IsShuffle(invocation, out string shuffleName) &&
                    CheckSelfLaneShuffle(invocation, shuffleName) is { } shuffleReason)
                {
                    return shuffleReason;
                }

                if (CheckUniformPath(invocation, method) is { } pathReason)
                {
                    return pathReason;
                }
            }
            else if (node is BreakStatementSyntax or ContinueStatementSyntax or GotoStatementSyntax or
                         ReturnStatementSyntax &&
                     CheckJump((StatementSyntax)node, method, splitMethodNames) is { } jumpReason)
            {
                return jumpReason;
            }
        }

        return null;
    }

    /// <summary>Returns whether <paramref name="invocation"/> is a barrier, a shuffle or a call of a split
    /// method.</summary>
    internal static bool IsSplitPoint(InvocationExpressionSyntax invocation, IReadOnlySet<string> splitMethodNames) =>
        FuzzSplitOracle.IsBarrier(invocation) ||
        FuzzSplitOracle.IsShuffle(invocation, out _) ||
        (FuzzSyntaxHelpers.GetInvokedMethodName(invocation) is { } name && splitMethodNames.Contains(name));

    private static string? CheckSelfLaneShuffle(InvocationExpressionSyntax invocation, string shuffleName)
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 3)
        {
            return $"{shuffleName} must take exactly a mask, a value and a source lane (no width)";
        }

        foreach (var argument in arguments)
        {
            if (argument.NameColon is not null || !argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                return $"{shuffleName} must take positional value arguments";
            }
        }

        string mask = Normalize(arguments[0].Expression);
        if (!string.Equals(mask, FuzzSplitOracle.FullMaskLiteral, StringComparison.OrdinalIgnoreCase))
        {
            return $"{shuffleName} must pass the full mask {FuzzSplitOracle.FullMaskLiteral}";
        }

        string lane = Normalize(arguments[2].Expression);
        bool selfLane = shuffleName switch
        {
            "ShuffleSync" => string.Equals(lane, s_selfLaneNormalized, StringComparison.Ordinal),
            "ShuffleXorSync" => lane == "0",
            _ => lane is "0" or "0u" or "0U",
        };
        return selfLane ? null : $"{shuffleName} must read the calling lane, not '{lane}'";
    }

    // Every construct between the split point and the method body must be taken the same way by all threads.
    private static string? CheckUniformPath(SyntaxNode splitPoint, MethodDeclarationSyntax method)
    {
        SyntaxNode child = splitPoint;
        for (var parent = splitPoint.Parent;
             parent is not null && parent != method;
             child = parent, parent = parent.Parent)
        {
            string? reason = parent switch
            {
                ConditionalExpressionSyntax conditional
                    when child != conditional.Condition && !IsUniform(conditional.Condition) =>
                    "a split point in an arm of ?: whose condition depends on the thread",
                BinaryExpressionSyntax binary
                    when IsShortCircuit(binary) && child == binary.Right && !IsUniform(binary.Left) =>
                    "a split point right of &&, || or ?? whose left side depends on the thread",
                IfStatementSyntax ifStatement
                    when child != ifStatement.Condition && !IsUniform(ifStatement.Condition) =>
                    "a split point under an if whose condition depends on the thread",
                ForStatementSyntax forStatement when !IsUniformCountedLoop(forStatement) =>
                    "a split point in a for loop that is not 'for (int k = 0; k < constant; k++)' with k unchanged",
                WhileStatementSyntax or DoStatementSyntax or CommonForEachStatementSyntax or SwitchStatementSyntax or
                    SwitchExpressionSyntax or AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or
                    TryStatementSyntax or LabeledStatementSyntax or UsingStatementSyntax or
                    ConditionalAccessExpressionSyntax =>
                    $"a split point inside {parent.Kind()}",
                _ => null,
            };
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    // A return is allowed only as the last statement of the method; break, continue and goto never leave or skip a
    // construct with a split point.
    private static string? CheckJump(
        StatementSyntax jump,
        MethodDeclarationSyntax method,
        IReadOnlySet<string> splitMethodNames)
    {
        if (jump is ReturnStatementSyntax)
        {
            bool isLast = method.Body is { } body && body.Statements.Count > 0 && body.Statements[^1] == jump;
            return !isLast && ContainsSplitPoint(method, splitMethodNames)
                ? "a return before the end of a method with a split point"
                : null;
        }

        if (jump is GotoStatementSyntax)
        {
            return ContainsSplitPoint(method, splitMethodNames) ? "a goto in a method with a split point" : null;
        }

        for (var parent = jump.Parent; parent is not null && parent != method; parent = parent.Parent)
        {
            bool isLoop = parent is ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
                CommonForEachStatementSyntax;
            if (isLoop || (jump is BreakStatementSyntax && parent is SwitchStatementSyntax))
            {
                return ContainsSplitPoint(parent, splitMethodNames)
                    ? $"a {jump.Kind()} that leaves a loop with a split point"
                    : null;
            }
        }

        return null;
    }

    private static bool ContainsSplitPoint(SyntaxNode node, IReadOnlySet<string> splitMethodNames)
    {
        foreach (var descendant in node.DescendantNodes())
        {
            if (descendant is InvocationExpressionSyntax invocation && IsSplitPoint(invocation, splitMethodNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsShortCircuit(BinaryExpressionSyntax binary) =>
        binary.IsKind(SyntaxKind.LogicalAndExpression) ||
        binary.IsKind(SyntaxKind.LogicalOrExpression) ||
        binary.IsKind(SyntaxKind.CoalesceExpression);

    // A uniform expression reads only BlockDimension / GridDimension and literals, with no call or write.
    private static bool IsUniform(ExpressionSyntax expression)
    {
        foreach (var node in expression.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case InvocationExpressionSyntax or AssignmentExpressionSyntax or ElementAccessExpressionSyntax:
                case PrefixUnaryExpressionSyntax prefix
                    when prefix.IsKind(SyntaxKind.PreIncrementExpression) ||
                         prefix.IsKind(SyntaxKind.PreDecrementExpression):
                case PostfixUnaryExpressionSyntax:
                    return false;
                case IdentifierNameSyntax identifier when !IsLaunchDimension(identifier):
                    return false;
            }
        }

        return true;
    }

    private static bool IsLaunchDimension(IdentifierNameSyntax identifier)
    {
        string name = identifier.Identifier.ValueText;
        if (name is "BlockDimension" or "GridDimension")
        {
            return identifier.Parent is MemberAccessExpressionSyntax access && access.Expression == identifier;
        }

        return name is "X" or "Y" or "Z" &&
            identifier.Parent is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "BlockDimension" or "GridDimension" },
            } owner &&
            owner.Name == identifier;
    }

    private static bool IsUniformCountedLoop(ForStatementSyntax loop)
    {
        if (loop.Declaration is not { Variables.Count: 1 } declaration ||
            declaration.Variables[0].Initializer?.Value is not LiteralExpressionSyntax ||
            loop.Condition is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LessThanExpression } condition ||
            condition.Left is not IdentifierNameSyntax conditionVariable ||
            condition.Right is not LiteralExpressionSyntax ||
            loop.Incrementors.Count != 1)
        {
            return false;
        }

        string variable = declaration.Variables[0].Identifier.ValueText;
        if (!string.Equals(conditionVariable.Identifier.ValueText, variable, StringComparison.Ordinal) ||
            !IsIncrementOf(loop.Incrementors[0], variable))
        {
            return false;
        }

        foreach (var node in loop.Statement.DescendantNodes())
        {
            if (node is IdentifierNameSyntax id &&
                string.Equals(id.Identifier.ValueText, variable, StringComparison.Ordinal) &&
                FuzzSyntaxHelpers.IsAssignedOrMutatedTarget(id))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIncrementOf(ExpressionSyntax incrementor, string variable) =>
        incrementor switch
        {
            PostfixUnaryExpressionSyntax { Operand: IdentifierNameSyntax id } postfix
                when postfix.IsKind(SyntaxKind.PostIncrementExpression) =>
                string.Equals(id.Identifier.ValueText, variable, StringComparison.Ordinal),
            PrefixUnaryExpressionSyntax { Operand: IdentifierNameSyntax id } prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression) =>
                string.Equals(id.Identifier.ValueText, variable, StringComparison.Ordinal),
            _ => false,
        };

    private static string Normalize(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        string text = FuzzSyntaxHelpers.RenderNodeText(expression);
        using var writer = new SourceWriter();
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                writer.Append(c);
            }
        }

        return writer.ToString();
    }
}
