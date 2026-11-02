using System.Linq;
using System.Text;
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

        foreach (var decl in cleanedMethod.Body.DescendantNodes().OfType<VariableDeclarationSyntax>())
        {
            string normType = NormalizeTypeSyntax(decl.Type, typeMap);
            foreach (var v in decl.Variables)
            {
                declaredLocals.Add(v.Identifier.ValueText);
                localTypes[v.Identifier.ValueText] = normType;
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

        string wrapped = $"class __Wrapper {{ {method.ToFullString()} }}";
        string hoistedWrapped = FuzzReducer.HoistLocalsToDefault(wrapped);
        var parsedMethod = CSharpSyntaxTree.ParseText(hoistedWrapped, FuzzSyntaxHelpers.ParseOptions)
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();

        if (parsedMethod.Body is null)
        {
            return method;
        }

        var statements = parsedMethod.Body.Statements.ToList();
        StripUnreadLocalAssignments(statements);
        RemoveUnreferencedHoistedDeclarations(statements);

        return parsedMethod.WithBody(parsedMethod.Body.WithStatements(SyntaxFactory.List(statements)));
    }

    internal static void AppendNormalizedStatement(
        StringBuilder sb,
        StatementSyntax stmt,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap,
        IReadOnlyDictionary<string, string> localTypes)
    {
        switch (stmt)
        {
            case BlockSyntax block:
                sb.Append('{');
                foreach (var inner in block.Statements)
                {
                    AppendNormalizedStatement(sb, inner, varMap, methodMap, typeMap, localTypes);
                }

                sb.Append('}');
                break;

            case LocalDeclarationStatementSyntax localDecl:
                string typeText = NormalizeTypeSyntax(localDecl.Declaration.Type, typeMap);
                foreach (var v in localDecl.Declaration.Variables)
                {
                    string vName = varMap.TryGetValue(v.Identifier.ValueText, out string? mapped) ? mapped : v.Identifier.ValueText;
                    sb.Append(typeText).Append(' ').Append(vName);
                    if (v.Initializer is not null)
                    {
                        sb.Append('=');
                        AppendNormalizedExpression(sb, v.Initializer.Value, varMap, methodMap, typeMap);
                    }

                    sb.Append(';');
                }

                break;

            case ExpressionStatementSyntax exprStmt:
                if (exprStmt.Expression is AssignmentExpressionSyntax { Left: IdentifierNameSyntax { Identifier.ValueText: "hash" } } hashAssign &&
                    TryNormalizeChecksumFold(hashAssign, varMap, methodMap, typeMap, out string? normalizedFold))
                {
                    sb.Append(normalizedFold).Append(';');
                }
                else
                {
                    AppendNormalizedExpression(sb, exprStmt.Expression, varMap, methodMap, typeMap);
                    sb.Append(';');
                }

                break;

            case IfStatementSyntax ifStmt:
                sb.Append("if(");
                AppendNormalizedExpression(sb, ifStmt.Condition, varMap, methodMap, typeMap);
                sb.Append(')');
                AppendNormalizedStatement(sb, ifStmt.Statement, varMap, methodMap, typeMap, localTypes);
                if (ifStmt.Else is not null)
                {
                    sb.Append("else ");
                    AppendNormalizedStatement(sb, ifStmt.Else.Statement, varMap, methodMap, typeMap, localTypes);
                }

                break;

            case ReturnStatementSyntax retStmt:
                sb.Append("return");
                if (retStmt.Expression is not null)
                {
                    sb.Append(' ');
                    AppendNormalizedExpression(sb, retStmt.Expression, varMap, methodMap, typeMap);
                }

                sb.Append(';');
                break;

            default:
                sb.Append(stmt.WithoutTrivia().ToFullString());
                break;
        }
    }

    internal static void AppendNormalizedExpression(
        StringBuilder sb,
        ExpressionSyntax expr,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        expr = UnwrapParentheses(expr);

        switch (expr)
        {
            case LiteralExpressionSyntax lit:
                sb.Append(NormalizeLiteralToken(lit));
                break;

            case DefaultExpressionSyntax defExpr:
                sb.Append("default(").Append(NormalizeTypeSyntax(defExpr.Type, typeMap)).Append(')');
                break;

            case IdentifierNameSyntax id:
                AppendNormalizedIdentifier(sb, id.Identifier.ValueText, varMap, methodMap, typeMap);
                break;

            case PredefinedTypeSyntax predType:
                sb.Append(predType.Keyword.ValueText);
                break;

            case MemberAccessExpressionSyntax member:
                AppendNormalizedMemberAccess(sb, member, varMap, methodMap, typeMap);
                break;

            case ElementAccessExpressionSyntax elem:
                AppendNormalizedElementAccess(sb, elem, varMap, methodMap, typeMap);
                break;

            case InvocationExpressionSyntax inv:
                AppendNormalizedInvocation(sb, inv, varMap, methodMap, typeMap);
                break;

            case CastExpressionSyntax cast:
                if (TryCollapseTypedNumericLiteralCast(cast, out string? castLitToken))
                {
                    sb.Append(castLitToken);
                }
                else
                {
                    sb.Append("((").Append(NormalizeTypeSyntax(cast.Type, typeMap)).Append(')');
                    AppendNormalizedExpression(sb, cast.Expression, varMap, methodMap, typeMap);
                    sb.Append(')');
                }

                break;

            case PrefixUnaryExpressionSyntax prefix:
                if (prefix.IsKind(SyntaxKind.UnaryMinusExpression) &&
                    UnwrapParentheses(prefix.Operand) is LiteralExpressionSyntax negLit &&
                    negLit.IsKind(SyntaxKind.NumericLiteralExpression))
                {
                    sb.Append(NormalizeNumericLiteralValue(negLit.Token));
                }
                else
                {
                    sb.Append('(').Append(prefix.OperatorToken.ValueText);
                    AppendNormalizedExpression(sb, prefix.Operand, varMap, methodMap, typeMap);
                    sb.Append(')');
                }

                break;

            case PostfixUnaryExpressionSyntax postfix:
                sb.Append('(');
                AppendNormalizedExpression(sb, postfix.Operand, varMap, methodMap, typeMap);
                sb.Append(postfix.OperatorToken.ValueText).Append(')');
                break;

            case BinaryExpressionSyntax binary:
                sb.Append('(');
                AppendNormalizedExpression(sb, binary.Left, varMap, methodMap, typeMap);
                sb.Append(binary.OperatorToken.ValueText);
                AppendNormalizedExpression(sb, binary.Right, varMap, methodMap, typeMap);
                sb.Append(')');
                break;

            case AssignmentExpressionSyntax assign:
                AppendNormalizedExpression(sb, assign.Left, varMap, methodMap, typeMap);
                sb.Append(assign.OperatorToken.ValueText);
                AppendNormalizedExpression(sb, assign.Right, varMap, methodMap, typeMap);
                break;

            case ConditionalExpressionSyntax cond:
                sb.Append('(');
                AppendNormalizedExpression(sb, cond.Condition, varMap, methodMap, typeMap);
                sb.Append('?');
                AppendNormalizedExpression(sb, cond.WhenTrue, varMap, methodMap, typeMap);
                sb.Append(':');
                AppendNormalizedExpression(sb, cond.WhenFalse, varMap, methodMap, typeMap);
                sb.Append(')');
                break;

            default:
                sb.Append(expr.WithoutTrivia().ToFullString());
                break;
        }
    }

    internal static string NormalizeTypeSyntax(TypeSyntax typeSyntax, IReadOnlyDictionary<string, string> typeMap)
    {
        return typeSyntax switch
        {
            ArrayTypeSyntax arr => $"{NormalizeTypeSyntax(arr.ElementType, typeMap)}[]",
            NullableTypeSyntax nul => $"{NormalizeTypeSyntax(nul.ElementType, typeMap)}?",
            RefTypeSyntax refType => $"ref {NormalizeTypeSyntax(refType.Type, typeMap)}",
            IdentifierNameSyntax id when typeMap.TryGetValue(id.Identifier.ValueText, out string? mapped) => mapped,
            _ => typeSyntax.WithoutTrivia().ToFullString(),
        };
    }

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

    private static bool HasSideEffectInRhs(ExpressionSyntax rhs) =>
        rhs.DescendantNodesAndSelf().Any(static node =>
            node is AssignmentExpressionSyntax ||
            (node is PrefixUnaryExpressionSyntax pre && (pre.IsKind(SyntaxKind.PreIncrementExpression) || pre.IsKind(SyntaxKind.PreDecrementExpression))) ||
            (node is PostfixUnaryExpressionSyntax post && (post.IsKind(SyntaxKind.PostIncrementExpression) || post.IsKind(SyntaxKind.PostDecrementExpression))));

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

            foreach (var id in stmt.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                referencedVars.Add(id.Identifier.ValueText);
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
        foreach (var id in stmt.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (id.Identifier.ValueText != varName)
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

    private static bool TryNormalizeChecksumFold(
        AssignmentExpressionSyntax hashAssign,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap,
        out string? normalizedFold)
    {
        string hashVar = varMap.TryGetValue("hash", out string? h) ? h : "hash";
        var castNodes = hashAssign.Right.DescendantNodes()
            .OfType<CastExpressionSyntax>()
            .Where(static c => c.Type.ToString() == "ulong")
            .ToList();

        if (castNodes.Count == 1)
        {
            var innerBuilder = new StringBuilder();
            AppendNormalizedExpression(innerBuilder, castNodes[0].Expression, varMap, methodMap, typeMap);
            normalizedFold = $"{hashVar}=FOLD({innerBuilder})";
            return true;
        }

        normalizedFold = null;
        return false;
    }

    private static void AppendNormalizedIdentifier(
        StringBuilder sb,
        string idText,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        if (varMap.TryGetValue(idText, out string? mappedVar))
        {
            sb.Append(mappedVar);
        }
        else if (methodMap.TryGetValue(idText, out string? mappedMethod))
        {
            sb.Append(mappedMethod);
        }
        else if (typeMap.TryGetValue(idText, out string? mappedType))
        {
            sb.Append(mappedType);
        }
        else
        {
            sb.Append(idText);
        }
    }

    private static void AppendNormalizedMemberAccess(
        StringBuilder sb,
        MemberAccessExpressionSyntax member,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        if (IsConstantBoundaryMemberAccess(member, out string? boundaryToken))
        {
            sb.Append(boundaryToken);
            return;
        }

        AppendNormalizedExpression(sb, member.Expression, varMap, methodMap, typeMap);
        sb.Append('.');
        string memberName = member.Name.Identifier.ValueText;
        sb.Append(s_enumMemberNamesSet.Contains(memberName) ? "LIT_ENUM" : memberName);
    }

    private static void AppendNormalizedElementAccess(
        StringBuilder sb,
        ElementAccessExpressionSyntax elem,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        AppendNormalizedExpression(sb, elem.Expression, varMap, methodMap, typeMap);
        sb.Append('[');
        for (int i = 0; i < elem.ArgumentList.Arguments.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            AppendNormalizedExpression(sb, elem.ArgumentList.Arguments[i].Expression, varMap, methodMap, typeMap);
        }

        sb.Append(']');
    }

    private static void AppendNormalizedInvocation(
        StringBuilder sb,
        InvocationExpressionSyntax inv,
        IReadOnlyDictionary<string, string> varMap,
        IReadOnlyDictionary<string, string> methodMap,
        IReadOnlyDictionary<string, string> typeMap)
    {
        AppendNormalizedExpression(sb, inv.Expression, varMap, methodMap, typeMap);
        sb.Append('(');
        for (int i = 0; i < inv.ArgumentList.Arguments.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            var arg = inv.ArgumentList.Arguments[i];
            if (arg.NameColon is not null)
            {
                sb.Append(arg.NameColon.Name.Identifier.ValueText).Append(':');
            }

            if (!arg.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                sb.Append(arg.RefKindKeyword.ValueText).Append(' ');
            }

            AppendNormalizedExpression(sb, arg.Expression, varMap, methodMap, typeMap);
        }

        sb.Append(')');
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
            token = MapScalarTypeToLiteralToken(cast.Type.WithoutTrivia().ToString());
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
            token = MapScalarTypeToLiteralToken(member.Expression.WithoutTrivia().ToString());
            if (token is not null)
            {
                return true;
            }
        }

        token = null;
        return false;
    }

    private static string? MapScalarTypeToLiteralToken(string typeName) => typeName switch
    {
        "sbyte" => "LIT_SBYTE",
        "byte" => "LIT_BYTE",
        "short" => "LIT_SHORT",
        "ushort" => "LIT_USHORT",
        "int" => "LIT_INT",
        "uint" => "LIT_UINT",
        "long" => "LIT_LONG",
        "ulong" => "LIT_ULONG",
        "char" => "LIT_CHAR",
        _ => null,
    };

    private static string NormalizeLiteralToken(LiteralExpressionSyntax lit) => lit.Kind() switch
    {
        SyntaxKind.TrueLiteralExpression or SyntaxKind.FalseLiteralExpression => "LIT_BOOL",
        SyntaxKind.CharacterLiteralExpression => "LIT_CHAR",
        SyntaxKind.DefaultLiteralExpression => "default",
        SyntaxKind.NullLiteralExpression => "null",
        SyntaxKind.NumericLiteralExpression => NormalizeNumericLiteralValue(lit.Token),
        _ => "LIT",
    };

    private static string NormalizeNumericLiteralValue(SyntaxToken token) => token.Value switch
    {
        sbyte => "LIT_SBYTE",
        byte => "LIT_BYTE",
        short => "LIT_SHORT",
        ushort => "LIT_USHORT",
        int => "LIT_INT",
        uint => "LIT_UINT",
        long => "LIT_LONG",
        ulong => "LIT_ULONG",
        char => "LIT_CHAR",
        float => "LIT_FLOAT",
        double => "LIT_DOUBLE",
        _ => "LIT_INT",
    };
}

