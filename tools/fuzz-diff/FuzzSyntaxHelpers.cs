using System.Globalization;
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

    internal static string RenderFullNode(SyntaxNode node)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        node.WriteTo(writer);
        return writer.ToString();
    }

    internal static string RenderNodeText(SyntaxNode node)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        node.WithoutTrivia().WriteTo(writer);
        return writer.ToString();
    }

    internal static List<string> SplitNonEmpty(ReadOnlySpan<char> value, ReadOnlySpan<char> separators)
    {
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || separators.Contains(value[i]))
            {
                if (i > start)
                {
                    parts.Add(value[start..i].ToString());
                }

                start = i + 1;
            }
        }

        return parts;
    }

    internal static List<MethodDeclarationSyntax> GetKernelMethods(CompilationUnitSyntax root)
    {
        var result = new List<MethodDeclarationSyntax>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax method && HasAttribute(method, "GpuKernel"))
            {
                result.Add(method);
            }
        }

        return result;
    }

    internal static List<MethodDeclarationSyntax> GetDeviceFunctionMethods(CompilationUnitSyntax root)
    {
        var result = new List<MethodDeclarationSyntax>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is MethodDeclarationSyntax method && HasAttribute(method, "GpuDeviceFunction"))
            {
                result.Add(method);
            }
        }

        return result;
    }

    internal static bool HasAttribute(MethodDeclarationSyntax method, string attributeShortName)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                string name = RenderNodeText(attr.Name);
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

        var statements = method.Body.Statements;
        var referencedIdentifiers = new HashSet<string>(StringComparer.Ordinal);
        var keepFlags = new bool[statements.Count];

        for (int i = statements.Count - 1; i >= 0; i--)
        {
            var stmt = statements[i];
            bool shouldKeep = !indicesToRemove.Contains(i);
            if (!shouldKeep &&
                stmt is LocalDeclarationStatementSyntax localDecl &&
                DeclaresAnyReferencedVariable(localDecl, referencedIdentifiers))
            {
                shouldKeep = true;
            }

            if (shouldKeep)
            {
                keepFlags[i] = true;
                foreach (var node in stmt.DescendantNodes())
                {
                    if (node is IdentifierNameSyntax id)
                    {
                        referencedIdentifiers.Add(id.Identifier.ValueText);
                    }
                }
            }
        }

        var kept = new List<StatementSyntax>(statements.Count);
        for (int i = 0; i < statements.Count; i++)
        {
            if (keepFlags[i])
            {
                kept.Add(statements[i]);
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
        return RenderFullNode(root.ReplaceNode(method, updatedMethod));
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

        var list = new List<StatementSyntax>(method.Body.Statements.Count);
        for (int i = 0; i < method.Body.Statements.Count; i++)
        {
            list.Add(i == stmtIdx ? replacement : method.Body.Statements[i]);
        }

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
        DeclaresVariableNamed(localDecl, "gid");

    internal static bool IsGidBoundsGuard(StatementSyntax stmt)
    {
        if (stmt is not IfStatementSyntax ifStmt ||
            !RenderNodeText(ifStmt.Condition).Contains("gid", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var node in ifStmt.Statement.DescendantNodesAndSelf())
        {
            if (node is ReturnStatementSyntax)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsHashInitDeclaration(StatementSyntax stmt) =>
        stmt is LocalDeclarationStatementSyntax localDecl &&
        DeclaresVariableNamed(localDecl, "hash");

    internal static bool IsFinalChecksumWrite(StatementSyntax stmt) =>
        stmt is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                Left: ElementAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "checksum" } },
            },
        };

    internal static bool IsHoistableScalarOrEnumType(TypeSyntax typeSyntax) =>
        IsHoistableLocalType(root: null, typeSyntax, avoidStructConversionDefault: true);

    internal static bool IsHoistableLocalType(
        CompilationUnitSyntax? root,
        TypeSyntax typeSyntax,
        bool avoidStructConversionDefault = true)
    {
        if (typeSyntax is PredefinedTypeSyntax)
        {
            return true;
        }

        string text = RenderNodeText(typeSyntax);
        if (text is "FuzzEnumByte" or "FuzzEnumShort" or "FuzzEnumInt" or "FuzzEnumULong" or "FuzzFlagsUShort")
        {
            return true;
        }

        if (avoidStructConversionDefault && HasConversionOperatorInStructHierarchy(root, text))
        {
            return false;
        }

        if (text is "FuzzStructL1" or "FuzzStructL2" or "FuzzPackedStruct1" or "FuzzPackedStruct4" or "FuzzNullableBag" or "FuzzWrapInt" or "FuzzStructL3")
        {
            return true;
        }

        if (root is not null)
        {
            foreach (var node in root.DescendantNodes())
            {
                if (node is BaseTypeDeclarationSyntax typeDecl &&
                    typeDecl is StructDeclarationSyntax or EnumDeclarationSyntax &&
                    string.Equals(typeDecl.Identifier.ValueText, text, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool HasConversionOperatorInStructHierarchy(CompilationUnitSyntax? root, string typeName)
    {
        if (typeName is "FuzzWrapInt" or "FuzzStructL3")
        {
            return true;
        }

        if (root is null)
        {
            return false;
        }

        var structs = new Dictionary<string, StructDeclarationSyntax>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            if (node is StructDeclarationSyntax structDecl)
            {
                structs[structDecl.Identifier.ValueText] = structDecl;
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        return CheckStructRecursive(typeName, structs, visited);

        static bool CheckStructRecursive(
            string currentType,
            IReadOnlyDictionary<string, StructDeclarationSyntax> structMap,
            HashSet<string> seen)
        {
            if (currentType is "FuzzWrapInt" or "FuzzStructL3")
            {
                return true;
            }

            if (!seen.Add(currentType) || !structMap.TryGetValue(currentType, out var decl))
            {
                return false;
            }

            foreach (var member in decl.Members)
            {
                if (member is ConversionOperatorDeclarationSyntax)
                {
                    return true;
                }
            }

            foreach (var member in decl.Members)
            {
                if (member is FieldDeclarationSyntax field)
                {
                    string fieldType = RenderNodeText(field.Declaration.Type);
                    if (CheckStructRecursive(fieldType, structMap, seen))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    internal static bool IsStackAllocSpanDeclaration(LocalDeclarationStatementSyntax localDecl)
    {
        string typeText = RenderNodeText(localDecl.Declaration.Type);
        if (typeText.StartsWith("Span<", StringComparison.Ordinal) ||
            typeText.StartsWith("ReadOnlySpan<", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var node in localDecl.DescendantNodes())
        {
            if (node is StackAllocArrayCreationExpressionSyntax)
            {
                return true;
            }
        }

        return false;
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
                DeclaresVariableNamed(localDecl, varName))
            {
                return RenderNodeText(localDecl.Declaration.Type);
            }
        }

        return null;
    }

    internal static bool IsAssignedOrMutatedTarget(IdentifierNameSyntax id) =>
        id.Parent switch
        {
            AssignmentExpressionSyntax assign => assign.Left == id,
            PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression),
            ArgumentSyntax arg => !arg.RefKindKeyword.IsKind(SyntaxKind.None),
            _ => false,
        };

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
        foreach (var node in root.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax inv &&
                !helper.Span.Contains(inv.Span) &&
                string.Equals(GetInvokedMethodName(inv), name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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
        foreach (var diag in tree.GetDiagnostics())
        {
            if (diag.Severity == DiagnosticSeverity.Error)
            {
                return false;
            }
        }

        return true;
    }

    private static bool DeclaresVariableNamed(LocalDeclarationStatementSyntax localDecl, string name)
    {
        foreach (var v in localDecl.Declaration.Variables)
        {
            if (string.Equals(v.Identifier.ValueText, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DeclaresAnyReferencedVariable(LocalDeclarationStatementSyntax localDecl, HashSet<string> referencedIdentifiers)
    {
        foreach (var v in localDecl.Declaration.Variables)
        {
            if (referencedIdentifiers.Contains(v.Identifier.ValueText))
            {
                return true;
            }
        }

        return false;
    }
}
