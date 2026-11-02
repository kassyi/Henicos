using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Shared Roslyn syntax-tree parsing, query predicates, and statement manipulation helpers
/// used across <see cref="FuzzReducer"/>, <see cref="FuzzInlinerAndSimplifier"/>, <see cref="FuzzSpeculativeBatchBuilder"/>,
/// and <see cref="FuzzDeduplicator"/>.
/// </summary>
internal static class FuzzSyntaxHelpers
{
    internal static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    internal static CompilationUnitSyntax ParseRoot(string source) =>
        (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(source, ParseOptions).GetRoot();

    internal static List<MethodDeclarationSyntax> GetKernelMethods(CompilationUnitSyntax root) =>
        root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static m => HasAttribute(m, "GpuKernel"))
            .ToList();

    internal static List<MethodDeclarationSyntax> GetDeviceFunctionMethods(CompilationUnitSyntax root) =>
        root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static m => HasAttribute(m, "GpuDeviceFunction"))
            .ToList();

    internal static bool HasAttribute(MethodDeclarationSyntax method, string attributeShortName)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                string name = attr.Name.ToString();
                if (string.Equals(name, attributeShortName, StringComparison.Ordinal) ||
                    string.Equals(name, $"{attributeShortName}Attribute", StringComparison.Ordinal) ||
                    name.EndsWith($".{attributeShortName}", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static int CountPrologueStatements(SyntaxList<StatementSyntax> statements)
    {
        int count = 0;
        if (statements.Count > 0 && IsGidDeclaration(statements[0]))
        {
            count = 1;
            if (statements.Count > 1 && IsGidBoundsGuard(statements[1]))
            {
                count = 2;
            }
        }

        return count;
    }

    internal static List<int> GetRemovableStatementIndices(SyntaxList<StatementSyntax> statements)
    {
        var indices = new List<int>(statements.Count);
        for (int i = 0; i < statements.Count; i++)
        {
            var stmt = statements[i];
            if (IsGidDeclaration(stmt) ||
                IsGidBoundsGuard(stmt) ||
                IsHashInitDeclaration(stmt) ||
                IsFinalChecksumWrite(stmt) ||
                IsHoistedDefaultDeclaration(stmt))
            {
                continue;
            }

            indices.Add(i);
        }

        return indices;
    }

    internal static MethodDeclarationSyntax RemoveStatementsFromMethodNode(
        MethodDeclarationSyntax method,
        IReadOnlySet<int> indicesToRemove)
    {
        if (method.Body is null)
        {
            return method;
        }

        var kept = new List<StatementSyntax>(method.Body.Statements.Count);
        for (int i = 0; i < method.Body.Statements.Count; i++)
        {
            if (!indicesToRemove.Contains(i))
            {
                kept.Add(method.Body.Statements[i]);
            }
        }

        return method.WithBody(method.Body.WithStatements(SyntaxFactory.List(kept)));
    }

    internal static string RemoveMethodStatementsByIndex(
        CompilationUnitSyntax root,
        MethodDeclarationSyntax method,
        HashSet<int> indicesToRemove)
    {
        var updatedMethod = RemoveStatementsFromMethodNode(method, indicesToRemove);
        return root.ReplaceNode(method, updatedMethod).ToFullString();
    }

    internal static MethodDeclarationSyntax ReplaceSingleStatement(
        MethodDeclarationSyntax method,
        int stmtIdx,
        StatementSyntax replacement)
    {
        if (method.Body is null)
        {
            return method;
        }

        var list = method.Body.Statements.ToList();
        list[stmtIdx] = replacement;
        return method.WithBody(method.Body.WithStatements(SyntaxFactory.List(list)));
    }

    internal static int CountMethodExecutableStatements(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
        {
            return 0;
        }

        int count = 0;
        foreach (var stmt in method.Body.Statements)
        {
            if (IsHoistedDefaultDeclaration(stmt) && !IsHashInitDeclaration(stmt) && !IsGidDeclaration(stmt))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    internal static bool IsGidDeclaration(StatementSyntax stmt) =>
        stmt is LocalDeclarationStatementSyntax localDecl &&
        localDecl.Declaration.Variables.Any(static v => v.Identifier.ValueText == "gid");

    internal static bool IsGidBoundsGuard(StatementSyntax stmt) =>
        stmt is IfStatementSyntax ifStmt &&
        ifStmt.Condition.ToString().Contains("gid", StringComparison.Ordinal) &&
        ifStmt.Statement.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>().Any();

    internal static bool IsHashInitDeclaration(StatementSyntax stmt) =>
        stmt is LocalDeclarationStatementSyntax localDecl &&
        localDecl.Declaration.Variables.Any(static v => v.Identifier.ValueText == "hash");

    internal static bool IsFinalChecksumWrite(StatementSyntax stmt) =>
        stmt is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                Left: ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "checksum" } },
            },
        };

    internal static bool IsHoistableScalarOrEnumType(TypeSyntax typeSyntax)
    {
        if (typeSyntax is PredefinedTypeSyntax)
        {
            return true;
        }

        string text = typeSyntax.ToString();
        return text is "FuzzEnumByte" or "FuzzEnumShort" or "FuzzEnumInt" or "FuzzEnumULong" or "FuzzFlagsUShort";
    }

    internal static bool IsStackAllocSpanDeclaration(LocalDeclarationStatementSyntax localDecl)
    {
        string typeText = localDecl.Declaration.Type.ToString();
        return typeText.StartsWith("Span<", StringComparison.Ordinal) ||
               typeText.StartsWith("ReadOnlySpan<", StringComparison.Ordinal) ||
               localDecl.DescendantNodes().OfType<StackAllocArrayCreationExpressionSyntax>().Any();
    }

    internal static bool IsHoistedDefaultDeclaration(StatementSyntax stmt)
    {
        if (stmt is not LocalDeclarationStatementSyntax localDecl ||
            localDecl.Declaration.Variables.Count != 1)
        {
            return false;
        }

        var v = localDecl.Declaration.Variables[0];
        if (v.Initializer is null)
        {
            return true;
        }

        return v.Initializer.Value is DefaultExpressionSyntax or LiteralExpressionSyntax { RawKind: (int)SyntaxKind.DefaultLiteralExpression };
    }

    internal static bool IsChecksumFoldStatement(StatementSyntax stmt) =>
        stmt is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                Left: IdentifierNameSyntax { Identifier.ValueText: "hash" },
            },
        };

    internal static string? FindHoistedLocalType(SyntaxList<StatementSyntax> statements, string varName)
    {
        foreach (var stmt in statements)
        {
            if (stmt is LocalDeclarationStatementSyntax localDecl &&
                localDecl.Declaration.Variables.Any(v => v.Identifier.ValueText == varName))
            {
                return localDecl.Declaration.Type.ToString();
            }
        }

        return null;
    }

    internal static bool IsAssignedOrMutatedTarget(IdentifierNameSyntax id)
    {
        return id.Parent switch
        {
            AssignmentExpressionSyntax assign => assign.Left == id,
            PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression),
            ArgumentSyntax arg => !arg.RefKindKeyword.IsKind(SyntaxKind.None),
            _ => false,
        };
    }

    internal static bool IsBooleanComparison(BinaryExpressionSyntax bin) =>
        bin.Kind() is SyntaxKind.EqualsExpression
            or SyntaxKind.NotEqualsExpression
            or SyntaxKind.LessThanExpression
            or SyntaxKind.LessThanOrEqualExpression
            or SyntaxKind.GreaterThanExpression
            or SyntaxKind.GreaterThanOrEqualExpression
            or SyntaxKind.LogicalAndExpression
            or SyntaxKind.LogicalOrExpression;

    internal static bool IsHelperReferencedOutsideItself(CompilationUnitSyntax root, MethodDeclarationSyntax helper)
    {
        string name = helper.Identifier.ValueText;
        return root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => !helper.Span.Contains(inv.Span) && GetInvokedMethodName(inv) == name);
    }

    internal static string? GetInvokedMethodName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.ValueText,
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        _ => null,
    };

    internal static bool IsMemberAccessRight(IdentifierNameSyntax id) =>
        id.Parent is MemberAccessExpressionSyntax member && member.Name == id;

    internal static bool HasValidSyntax(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions);
        return !tree.GetDiagnostics().Any(static d => d.Severity == DiagnosticSeverity.Error);
    }
}

