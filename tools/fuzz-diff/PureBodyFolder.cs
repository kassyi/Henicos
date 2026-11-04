using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Folds the body of a pure device function into one expression over its parameters: each local declaration and
/// assignment is substituted into the statements after it, up to the <c>return</c>.
/// </summary>
/// <example>
/// <c>{ int step = (a ^ 5) + b; return (step * 3) ^ a; }</c> folds to
/// <c>(((int)(((a ^ 5) + b))) * 3) ^ a</c>.
/// </example>
internal static class PureBodyFolder
{
    private enum FoldStep
    {
        Continue,
        Returned,
        Reject,
    }

    /// <summary>
    /// Folds <paramref name="helper"/> into <paramref name="returnTemplate"/>, an expression that still names the
    /// parameters. Returns <see langword="false"/> when a statement is not a declaration of a scalar or enum local, an
    /// assignment to a local, or a <c>return</c>, or when an expression in it is not pure.
    /// </summary>
    internal static bool TryFold(
        MethodDeclarationSyntax helper,
        SemanticModel semanticModel,
        [NotNullWhen(true)] out string? returnTemplate)
    {
        var scope = EffectScope.WithoutInputs(semanticModel);
        if (helper.ExpressionBody is not null)
        {
            return TryFoldExpressionBody(helper.ExpressionBody.Expression, scope, out returnTemplate);
        }

        returnTemplate = null;
        if (helper.Body is null)
        {
            return false;
        }

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        string? folded = null;
        foreach (var statement in helper.Body.Statements)
        {
            var step = statement switch
            {
                LocalDeclarationStatementSyntax declaration => FoldDeclaration(declaration, scope, env),
                ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } => FoldAssignment(assignment, scope, env),
                ReturnStatementSyntax { Expression: { } value } => FoldReturn(value, scope, env, out folded),
                _ => FoldStep.Reject,
            };
            if (step == FoldStep.Returned && folded is not null)
            {
                returnTemplate = folded;
                return true;
            }

            if (step != FoldStep.Continue)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces every identifier of <paramref name="exprText"/> that is a key of <paramref name="map"/> (except the
    /// member name of <c>a.b</c>) with its value.
    /// </summary>
    internal static string SubstituteUsingMap(string exprText, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0)
        {
            return exprText;
        }

        var expr = SyntaxFactory.ParseExpression(exprText);
        var ids = new List<IdentifierNameSyntax>();
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is IdentifierNameSyntax id &&
                !FuzzSyntaxHelpers.IsMemberAccessRight(id) &&
                map.ContainsKey(id.Identifier.ValueText))
            {
                ids.Add(id);
            }
        }

        if (ids.Count == 0)
        {
            return exprText;
        }

        var replaced = expr.ReplaceNodes(ids, (orig, _) =>
            SyntaxFactory.ParseExpression(map[orig.Identifier.ValueText]).WithTriviaFrom(orig));
        return FuzzSyntaxHelpers.RenderFullNode(replaced);
    }

    private static bool TryFoldExpressionBody(
        ExpressionSyntax body,
        EffectScope scope,
        [NotNullWhen(true)] out string? returnTemplate)
    {
        if (!MetamorphicPurity.IsPure(body, scope))
        {
            returnTemplate = null;
            return false;
        }

        returnTemplate = FuzzSyntaxHelpers.RenderNodeText(body);
        return true;
    }

    // T v = <init>;  ->  env[v] = ((T)(<init>)), or ((T)(default(T))) without an initializer
    private static FoldStep FoldDeclaration(
        LocalDeclarationStatementSyntax declaration,
        EffectScope scope,
        Dictionary<string, string> env)
    {
        if (declaration.Declaration.Type is RefTypeSyntax ||
            FuzzSyntaxHelpers.IsStackAllocSpanDeclaration(declaration))
        {
            return FoldStep.Reject;
        }

        foreach (var v in declaration.Declaration.Variables)
        {
            if (scope.Model.GetDeclaredSymbol(v) is not ILocalSymbol localSym ||
                !MetamorphicTypeResolver.IsSupportedScalarOrEnumValueType(localSym.Type))
            {
                return FoldStep.Reject;
            }

            string typeText = MetamorphicTypeResolver.FormatCSharpType(localSym.Type);
            string initText;
            if (v.Initializer is not null)
            {
                if (!MetamorphicPurity.IsPure(v.Initializer.Value, scope))
                {
                    return FoldStep.Reject;
                }

                initText = SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(v.Initializer.Value), env);
            }
            else
            {
                initText = $"default({typeText})";
            }

            env[localSym.Name] = $"(({typeText})({initText}))";
        }

        return FoldStep.Continue;
    }

    // v = <rhs>;  ->  env[v] = ((T)(<rhs>));    v op= <rhs>;  ->  env[v] = ((T)((<v>) op (<rhs>)))
    private static FoldStep FoldAssignment(
        AssignmentExpressionSyntax assignment,
        EffectScope scope,
        Dictionary<string, string> env)
    {
        if (assignment.Left is not IdentifierNameSyntax target ||
            assignment.Kind() is SyntaxKind.DivideAssignmentExpression or SyntaxKind.ModuloAssignmentExpression ||
            !MetamorphicPurity.IsPure(assignment.Right, scope) ||
            !MetamorphicTypeResolver.TryResolveSupportedSymbolValueType(
                scope.Model.GetSymbolInfo(target).Symbol,
                out _,
                out string? targetTypeText))
        {
            return FoldStep.Reject;
        }

        string targetName = target.Identifier.ValueText;
        string rhsText = SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(assignment.Right), env);
        if (assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            env[targetName] = $"(({targetTypeText})({rhsText}))";
        }
        else
        {
            string previous = env.TryGetValue(targetName, out string? existing) ? existing : targetName;
            string binaryOperator = assignment.OperatorToken.ValueText[..^1];
            env[targetName] = $"(({targetTypeText})(({previous}) {binaryOperator} ({rhsText})))";
        }

        return FoldStep.Continue;
    }

    // return <value>;  ->  the folded <value>
    private static FoldStep FoldReturn(
        ExpressionSyntax value,
        EffectScope scope,
        Dictionary<string, string> env,
        out string? returnTemplate)
    {
        if (!MetamorphicPurity.IsPure(value, scope))
        {
            returnTemplate = null;
            return FoldStep.Reject;
        }

        returnTemplate = SubstituteUsingMap(FuzzSyntaxHelpers.RenderNodeText(value), env);
        return FoldStep.Returned;
    }
}
