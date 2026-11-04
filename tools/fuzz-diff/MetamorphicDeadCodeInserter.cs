using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Inserts dead code (an unused local variable initialized with a pure integer expression
/// and an unreachable <c>if (false)</c> branch free of barriers or warp calls) into a target <c>[GpuKernel]</c> body.
/// </summary>
internal static class MetamorphicDeadCodeInserter
{
    internal static CompilationUnitSyntax Apply(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        ulong seed,
        string? targetMethodName)
    {
        var method = MetamorphicTarget.FindKernel(root, targetMethodName);
        if (method?.Body is null || MetamorphicTypeResolver.HasSemanticErrorsInMethod(method, semanticModel))
        {
            return root;
        }

        var statements = method.Body.Statements;
        int minInsertIdx = FuzzSyntaxHelpers.CountPrologueStatements(statements);
        if (minInsertIdx < statements.Count && FuzzSyntaxHelpers.IsHashInitDeclaration(statements[minInsertIdx]))
        {
            minInsertIdx++;
        }

        // Do not insert dead code into an empty method or a method that has no executable statements after its prologue.
        if (statements.Count == 0 || minInsertIdx >= statements.Count)
        {
            return root;
        }

        int maxInsertIdx = statements.Count;
        if (maxInsertIdx > minInsertIdx && FuzzSyntaxHelpers.IsFinalChecksumWrite(statements[maxInsertIdx - 1]))
        {
            maxInsertIdx--;
        }

        var rng = new FuzzRandom(seed ^ 0xA11CE008UL);
        var usedNames = MetamorphicTarget.CollectIdentifiers(root);

        bool hasIntGid = EffectScope.ResolveGuardedGid(method, semanticModel) is ILocalSymbol
        {
            Type.SpecialType: SpecialType.System_Int32,
        };
        bool hasULongHash = HasULongHashLocal(method, semanticModel);

        int insertIdx = maxInsertIdx > minInsertIdx
            ? minInsertIdx + rng.NextBelow(maxInsertIdx - minInsertIdx + 1)
            : minInsertIdx;

        string deadLocalName = MetamorphicTarget.AllocateFreshIdentifier("metaDeadLocal", usedNames);
        string seedConstHex = string.Create(CultureInfo.InvariantCulture, $"0x{((rng.NextUInt32() & 0x7FFFFFFFU) | 1U):X8}");
        string baseIntExpr = hasIntGid ? "gid" : "17";

        var deadLocalStmt = SyntaxFactory.ParseStatement(
            string.Create(CultureInfo.InvariantCulture, $"int {deadLocalName} = ({baseIntExpr} ^ {seedConstHex}) + 29;\n"));

        string unreachableBody = hasULongHash
            ? $"if (false)\n{{\n{deadLocalName} ^= 0x13579BDF;\nhash ^= (ulong){deadLocalName};\n}}\n"
            : $"if (false)\n{{\n{deadLocalName} ^= 0x13579BDF;\n}}\n";
        var unreachableIfStmt = SyntaxFactory.ParseStatement(unreachableBody);

        var newStatements = new List<StatementSyntax>(statements.Count + 2);
        for (int i = 0; i <= statements.Count; i++)
        {
            if (i == insertIdx)
            {
                newStatements.Add(deadLocalStmt);
                newStatements.Add(unreachableIfStmt);
            }

            if (i < statements.Count)
            {
                newStatements.Add(statements[i]);
            }
        }

        var updatedMethod = method.WithBody(method.Body.WithStatements(SyntaxFactory.List(newStatements)));
        return root.ReplaceNode(method, updatedMethod);
    }

    private static bool HasULongHashLocal(MethodDeclarationSyntax method, SemanticModel semanticModel)
    {
        if (method.Body is null)
        {
            return false;
        }

        foreach (var stmt in method.Body.Statements)
        {
            if (stmt is LocalDeclarationStatementSyntax localDecl)
            {
                foreach (var v in localDecl.Declaration.Variables)
                {
                    if (string.Equals(v.Identifier.ValueText, "hash", StringComparison.Ordinal) &&
                        semanticModel.GetDeclaredSymbol(v) is ILocalSymbol
                        {
                            IsRef: false,
                            Type.SpecialType: SpecialType.System_UInt64,
                        })
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
