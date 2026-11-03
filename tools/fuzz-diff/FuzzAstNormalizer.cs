using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Dead-store stripping, variable alpha-renaming, statement/expression AST canonicalization,
/// and literal token normalization used by <see cref="FuzzDeduplicator"/>.
/// </summary>
internal static class FuzzAstNormalizer
{
    private static readonly HashSet<string> s_enumMemberNamesSet = new(StringComparer.Ordinal)
    {
        "Zero", "One", "Two", "Bit", "High", "Mask", "Alt", "Edge",
    };

    internal static (Dictionary<string, string> VarMap, Dictionary<string, string> LocalTypes) BuildVariableNormalizationMap(
        MethodDeclarationSyntax cleanedMethod,
        IReadOnlyDictionary<string, string> typeMap)
    {
        var varMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var param in cleanedMethod.ParameterList.Parameters)
        {
            string pName = param.Identifier.ValueText;
            if (!varMap.ContainsKey(pName))
            {
                varMap[pName] = $"v{varMap.Count}";
            }
        }

        var declaredLocals = new HashSet<string>(StringComparer.Ordinal);
        var localTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (cleanedMethod.Body is null)
        {
            return (varMap, localTypes);
        }

        foreach (var node in cleanedMethod.Body.DescendantNodes())
        {
            if (node is VariableDeclarationSyntax decl)
            {
                string normType = NormalizeTypeSyntax(decl.Type, typeMap);
                foreach (var v in decl.Variables)
                {
                    declaredLocals.Add(v.Identifier.ValueText);
                    localTypes[v.Identifier.ValueText] = normType;
                }
            }
        }

        foreach (var stmt in cleanedMethod.Body.Statements)
        {
            if (FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(stmt) &&
                !FuzzSyntaxHelpers.IsGidDeclaration(stmt) &&
                !FuzzSyntaxHelpers.IsHashInitDeclaration(stmt))
            {
                continue;
            }

            foreach (var token in stmt.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.IdentifierToken))
                {
                    string text = token.ValueText;
                    if (declaredLocals.Contains(text) && !varMap.ContainsKey(text))
                    {
                        varMap[text] = $"v{varMap.Count}";
                    }
                }
            }
        }

        return (varMap, localTypes);
    }

    internal static MethodDeclarationSyntax StripDeadLocalsAndStores(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
        {
            return method;
        }

        string wrapped = $"class __Wrapper {{ {FuzzSyntaxHelpers.RenderFullNode(method)} }}";
        string hoistedWrapped = FuzzReducer.HoistLocalsToDefault(wrapped);
        MethodDeclarationSyntax? parsedMethod = null;
        foreach (var node in CSharpSyntaxTree.ParseText(hoistedWrapped, FuzzSyntaxHelpers.ParseOptions).GetRoot().DescendantNodes())
        {
            if (node is MethodDeclarationSyntax m)
            {
                parsedMethod = m;
                break;
            }
        }

        if (parsedMethod?.Body is null)
        {
            return method;
        }

        var statements = new List<StatementSyntax>(parsedMethod.Body.Statements.Count);
        foreach (var stmt in parsedMethod.Body.Statements)
        {
            statements.Add(stmt);
        }

        StripUnreadLocalAssignments(statements);
        RemoveUnreferencedHoistedDeclarations(statements);

        return parsedMethod.WithBody(parsedMethod.Body.WithStatements(SyntaxFactory.List(statements)));
    }

    internal static void AppendNormalizedStatement(
        SourceWriter writer,
        StatementSyntax stmt,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap,
        IReadOnlyDictionary<string, string> localTypes)
    {
        _ = localTypes;
        var walker = new AstNormalizationWalker(writer, varMap, methodMap, typeMap);
        walker.Visit(stmt);
    }

    internal static void AppendNormalizedExpression(
        SourceWriter writer,
        ExpressionSyntax expr,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        var walker = new AstNormalizationWalker(writer, varMap, methodMap, typeMap);
        walker.Visit(expr);
    }

    internal static string NormalizeTypeSyntax(TypeSyntax typeSyntax, IReadOnlyDictionary<string, string> typeMap) =>
        typeSyntax switch
        {
            ArrayTypeSyntax arr => $"{NormalizeTypeSyntax(arr.ElementType, typeMap)}[]",
            NullableTypeSyntax nul => $"{NormalizeTypeSyntax(nul.ElementType, typeMap)}?",
            RefTypeSyntax refType => $"ref {NormalizeTypeSyntax(refType.Type, typeMap)}",
            IdentifierNameSyntax id when typeMap.TryGetValue(id.Identifier.ValueText, out string? mapped) => mapped,
            _ => FuzzSyntaxHelpers.RenderNodeText(typeSyntax),
        };

    private static void StripUnreadLocalAssignments(List<StatementSyntax> statements)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool removedAny = false;
            for (int i = statements.Count - 1; i >= 0; i--)
            {
                var stmt = statements[i];
                if (FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(stmt) ||
                    FuzzSyntaxHelpers.IsGidDeclaration(stmt) ||
                    FuzzSyntaxHelpers.IsGidBoundsGuard(stmt) ||
                    FuzzSyntaxHelpers.IsHashInitDeclaration(stmt) ||
                    FuzzSyntaxHelpers.IsFinalChecksumWrite(stmt))
                {
                    continue;
                }

                if (stmt is not ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax { Left: IdentifierNameSyntax leftId } assign })
                {
                    continue;
                }

                string targetVar = leftId.Identifier.ValueText;
                if (targetVar is "gid" or "hash" || HasSideEffectInRhs(assign.Right))
                {
                    continue;
                }

                bool readLater = false;
                for (int j = i + 1; j < statements.Count; j++)
                {
                    if (DoesStatementReadVariable(statements[j], targetVar))
                    {
                        readLater = true;
                        break;
                    }
                }

                if (!readLater)
                {
                    statements.RemoveAt(i);
                    removedAny = true;
                }
            }

            if (!removedAny)
            {
                break;
            }
        }
    }

    private static bool HasSideEffectInRhs(ExpressionSyntax rhs)
    {
        foreach (var node in rhs.DescendantNodesAndSelf())
        {
            if (node is AssignmentExpressionSyntax ||
                (node is PrefixUnaryExpressionSyntax pre && (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression))) ||
                (node is PostfixUnaryExpressionSyntax post && (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression))))
            {
                return true;
            }
        }

        return false;
    }

    private static void RemoveUnreferencedHoistedDeclarations(List<StatementSyntax> statements)
    {
        var referencedVars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stmt in statements)
        {
            if (FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(stmt) &&
                !FuzzSyntaxHelpers.IsGidDeclaration(stmt) &&
                !FuzzSyntaxHelpers.IsHashInitDeclaration(stmt))
            {
                continue;
            }

            foreach (var node in stmt.DescendantNodes())
            {
                if (node is IdentifierNameSyntax id)
                {
                    referencedVars.Add(id.Identifier.ValueText);
                }
            }
        }

        statements.RemoveAll(stmt =>
            FuzzSyntaxHelpers.IsHoistedDefaultDeclaration(stmt) &&
            !FuzzSyntaxHelpers.IsGidDeclaration(stmt) &&
            !FuzzSyntaxHelpers.IsHashInitDeclaration(stmt) &&
            stmt is LocalDeclarationStatementSyntax ld &&
            ld.Declaration.Variables.Count == 1 &&
            !referencedVars.Contains(ld.Declaration.Variables[0].Identifier.ValueText));
    }

    private static bool DoesStatementReadVariable(StatementSyntax stmt, string varName)
    {
        foreach (var node in stmt.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax id || id.Identifier.ValueText != varName)
            {
                continue;
            }

            if (id.Parent is AssignmentExpressionSyntax { RawKind: (int)SyntaxKind.SimpleAssignmentExpression } simpleAssign &&
                simpleAssign.Left == id)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expr)
    {
        while (expr is ParenthesizedExpressionSyntax paren)
        {
            expr = paren.Expression;
        }

        return expr;
    }

    private static bool TryCollapseTypedNumericLiteralCast(CastExpressionSyntax cast, out string? token)
    {
        var inner = UnwrapParentheses(cast.Expression);
        if (inner is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.UnaryMinusExpression } neg)
        {
            inner = UnwrapParentheses(neg.Operand);
        }

        if (inner is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NumericLiteralExpression })
        {
            token = FuzzTypeInfo.TryGetLiteralTokenByKeyword(FuzzSyntaxHelpers.RenderNodeText(cast.Type));
            return token is not null;
        }

        token = null;
        return false;
    }

    private static bool IsConstantBoundaryMemberAccess(MemberAccessExpressionSyntax member, out string? token)
    {
        string memberName = member.Name.Identifier.ValueText;
        if (memberName is "MinValue" or "MaxValue")
        {
            token = FuzzTypeInfo.TryGetLiteralTokenByKeyword(FuzzSyntaxHelpers.RenderNodeText(member.Expression));
            if (token is not null)
            {
                return true;
            }
        }

        token = null;
        return false;
    }

    private static string NormalizeLiteralToken(LiteralExpressionSyntax lit) => lit.Kind() switch
    {
        SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression => FuzzTypeInfo.Get(FuzzScalarKind.Boolean).LiteralToken,
        SyntaxKind.CharacterLiteralExpression => FuzzTypeInfo.Get(FuzzScalarKind.Char).LiteralToken,
        SyntaxKind.DefaultLiteralExpression => "default",
        SyntaxKind.NullLiteralExpression => "null",
        SyntaxKind.NumericLiteralExpression => FuzzTypeInfo.GetLiteralTokenForValue(lit.Token.Value),
        _ => "LIT",
    };

    private sealed class AstNormalizationWalker(
        SourceWriter writer,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap) : CSharpSyntaxWalker
    {
        public override void DefaultVisit(SyntaxNode node)
        {
            writer.Append(FuzzSyntaxHelpers.RenderNodeText(node));
        }

        public override void VisitBlock(BlockSyntax node)
        {
            writer.Append('{');
            foreach (var inner in node.Statements)
            {
                Visit(inner);
            }

            writer.Append('}');
        }

        public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
        {
            string typeText = NormalizeTypeSyntax(node.Declaration.Type, typeMap);
            foreach (var v in node.Declaration.Variables)
            {
                string vName = varMap.TryGetValue(v.Identifier.ValueText, out string? mapped) ? mapped : v.Identifier.ValueText;
                writer.Append(typeText);
                writer.Append(' ');
                writer.Append(vName);
                if (v.Initializer is not null)
                {
                    writer.Append('=');
                    Visit(v.Initializer.Value);
                }

                writer.Append(';');
            }
        }

        public override void VisitExpressionStatement(ExpressionStatementSyntax node)
        {
            if (node.Expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "hash" } } hashAssign &&
                TryNormalizeChecksumFold(hashAssign, out string? normalizedFold))
            {
                writer.Append(normalizedFold);
                writer.Append(';');
                return;
            }

            Visit(node.Expression);
            writer.Append(';');
        }

        public override void VisitIfStatement(IfStatementSyntax node)
        {
            writer.Append("if(");
            Visit(node.Condition);
            writer.Append(')');
            Visit(node.Statement);
            if (node.Else is not null)
            {
                writer.Append("else ");
                Visit(node.Else.Statement);
            }
        }

        public override void VisitReturnStatement(ReturnStatementSyntax node)
        {
            writer.Append("return");
            if (node.Expression is not null)
            {
                writer.Append(' ');
                Visit(node.Expression);
            }

            writer.Append(';');
        }

        public override void VisitParenthesizedExpression(ParenthesizedExpressionSyntax node)
        {
            Visit(UnwrapParentheses(node));
        }

        public override void VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            writer.Append(NormalizeLiteralToken(node));
        }

        public override void VisitDefaultExpression(DefaultExpressionSyntax node)
        {
            writer.Append("default(");
            writer.Append(NormalizeTypeSyntax(node.Type, typeMap));
            writer.Append(')');
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            string idText = node.Identifier.ValueText;
            if (varMap.TryGetValue(idText, out string? mappedVar))
            {
                writer.Append(mappedVar);
            }
            else if (methodMap.TryGetValue(idText, out string? mappedMethod))
            {
                writer.Append(mappedMethod);
            }
            else if (typeMap.TryGetValue(idText, out string? mappedType))
            {
                writer.Append(mappedType);
            }
            else
            {
                writer.Append(idText);
            }
        }

        public override void VisitPredefinedType(PredefinedTypeSyntax node)
        {
            writer.Append(node.Keyword.ValueText);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (IsConstantBoundaryMemberAccess(node, out string? boundaryToken))
            {
                writer.Append(boundaryToken);
                return;
            }

            Visit(node.Expression);
            writer.Append('.');
            string memberName = node.Name.Identifier.ValueText;
            writer.Append(s_enumMemberNamesSet.Contains(memberName) ? "LIT_ENUM" : memberName);
        }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            Visit(node.Expression);
            writer.Append('[');
            for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    writer.Append(',');
                }

                Visit(node.ArgumentList.Arguments[i].Expression);
            }

            writer.Append(']');
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            Visit(node.Expression);
            writer.Append('(');
            for (int i = 0; i < node.ArgumentList.Arguments.Count; i++)
            {
                if (i > 0)
                {
                    writer.Append(',');
                }

                var arg = node.ArgumentList.Arguments[i];
                if (arg.NameColon is not null)
                {
                    writer.Append(arg.NameColon.Name.Identifier.ValueText);
                    writer.Append(':');
                }

                if (!arg.RefKindKeyword.IsKind(SyntaxKind.None))
                {
                    writer.Append(arg.RefKindKeyword.ValueText);
                    writer.Append(' ');
                }

                Visit(arg.Expression);
            }

            writer.Append(')');
        }

        public override void VisitCastExpression(CastExpressionSyntax node)
        {
            if (TryCollapseTypedNumericLiteralCast(node, out string? castLitToken))
            {
                writer.Append(castLitToken);
                return;
            }

            writer.Append("((");
            writer.Append(NormalizeTypeSyntax(node.Type, typeMap));
            writer.Append(')');
            Visit(node.Expression);
            writer.Append(')');
        }

        public override void VisitPrefixUnaryExpression(PrefixUnaryExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.UnaryMinusExpression) &&
                UnwrapParentheses(node.Operand) is LiteralExpressionSyntax negLit &&
                negLit.IsKind(SyntaxKind.NumericLiteralExpression))
            {
                writer.Append(FuzzTypeInfo.GetLiteralTokenForValue(negLit.Token.Value));
                return;
            }

            writer.Append('(');
            writer.Append(node.OperatorToken.ValueText);
            Visit(node.Operand);
            writer.Append(')');
        }

        public override void VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node)
        {
            writer.Append('(');
            Visit(node.Operand);
            writer.Append(node.OperatorToken.ValueText);
            writer.Append(')');
        }

        public override void VisitBinaryExpression(BinaryExpressionSyntax node)
        {
            writer.Append('(');
            Visit(node.Left);
            writer.Append(node.OperatorToken.ValueText);
            Visit(node.Right);
            writer.Append(')');
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            Visit(node.Left);
            writer.Append(node.OperatorToken.ValueText);
            Visit(node.Right);
        }

        public override void VisitConditionalExpression(ConditionalExpressionSyntax node)
        {
            writer.Append('(');
            Visit(node.Condition);
            writer.Append('?');
            Visit(node.WhenTrue);
            writer.Append(':');
            Visit(node.WhenFalse);
            writer.Append(')');
        }

        private bool TryNormalizeChecksumFold(
            AssignmentExpressionSyntax hashAssign,
            [NotNullWhen(true)] out string? normalizedFold)
        {
            string hashVar = varMap.TryGetValue("hash", out string? h) ? h : "hash";
            CastExpressionSyntax? ulongCast = null;
            int count = 0;
            foreach (var node in hashAssign.Right.DescendantNodes())
            {
                if (node is CastExpressionSyntax cast &&
                    string.Equals(FuzzSyntaxHelpers.RenderNodeText(cast.Type), "ulong", StringComparison.Ordinal))
                {
                    ulongCast = cast;
                    count++;
                }
            }

            if (count == 1 && ulongCast is not null)
            {
                using var innerWriter = new SourceWriter();
                var innerWalker = new AstNormalizationWalker(innerWriter, varMap, methodMap, typeMap);
                innerWalker.Visit(ulongCast.Expression);
                normalizedFold = $"{hashVar}=FOLD({innerWriter})";
                return true;
            }

            normalizedFold = null;
            return false;
        }
    }
}
