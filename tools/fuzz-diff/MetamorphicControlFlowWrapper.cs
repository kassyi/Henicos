using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Wraps an executable non-declaring statement that contains no barrier, warp,
/// or jump constructs in a lexical block <c>{ S }</c>, <c>if (true) { S }</c>, or a single-iteration loop.
/// </summary>
internal static class MetamorphicControlFlowWrapper
{
    internal static CompilationUnitSyntax Apply(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        ulong seed,
        string? targetMethodName,
        MetamorphicWrapKind? wrapKind)
    {
        var method = MetamorphicTarget.FindKernel(root, targetMethodName);
        if (method?.Body is null || MetamorphicTypeResolver.HasSemanticErrorsInMethod(method, semanticModel))
        {
            return root;
        }

        var rng = new FuzzRandom(seed ^ 0xA11CE007UL);
        var usedNames = MetamorphicTarget.CollectIdentifiers(root);
        var statements = method.Body.Statements;

        var candidateIndices = new List<int>();
        for (int i = 0; i < statements.Count; i++)
        {
            if (IsWrappableExecutableStatement(statements[i], semanticModel, root))
            {
                candidateIndices.Add(i);
            }
        }

        if (candidateIndices.Count == 0)
        {
            return root;
        }

        int chosenStmtIdx = candidateIndices[rng.NextBelow(candidateIndices.Count)];
        var effectiveWrapKind = wrapKind ?? (MetamorphicWrapKind)rng.NextBelow(3);
        string stmtText = FuzzSyntaxHelpers.RenderNodeText(statements[chosenStmtIdx]);

        string wrappedText;
        switch (effectiveWrapKind)
        {
            case MetamorphicWrapKind.Block:
                wrappedText =
                    $$"""
                    {
                    {{stmtText}}
                    }
                    """;
                break;

            case MetamorphicWrapKind.IfTrue:
                wrappedText =
                    $$"""
                    if (true)
                    {
                    {{stmtText}}
                    }
                    """;
                break;

            default:
                string loopVar = MetamorphicTarget.AllocateFreshIdentifier("metaOnce", usedNames);
                wrappedText =
                    $$"""
                    for (int {{loopVar}} = 0; {{loopVar}} < 1; {{loopVar}}++)
                    {
                    {{stmtText}}
                    }
                    """;
                break;
        }

        var wrappedStmt = SyntaxFactory.ParseStatement(wrappedText + "\n");
        var updatedMethod = FuzzSyntaxHelpers.ReplaceSingleStatement(method, chosenStmtIdx, wrappedStmt);
        return root.ReplaceNode(method, updatedMethod);
    }

    private static bool IsWrappableExecutableStatement(
        StatementSyntax stmt,
        SemanticModel semanticModel,
        CompilationUnitSyntax root)
    {
        if (stmt is LocalDeclarationStatementSyntax or LabeledStatementSyntax or ReturnStatementSyntax ||
            FuzzSyntaxHelpers.IsGidDeclaration(stmt) ||
            FuzzSyntaxHelpers.IsGidBoundsGuard(stmt) ||
            FuzzSyntaxHelpers.IsHashInitDeclaration(stmt) ||
            FuzzSyntaxHelpers.IsFinalChecksumWrite(stmt))
        {
            return false;
        }

        // Never wrap a statement that directly, within nested blocks, or transitively through a helper
        // invokes a barrier, warp intrinsic, or GPU synchronization primitive.
        if (MetamorphicBarrierDetector.ContainsBarrierOrWarpCall(stmt, semanticModel, root))
        {
            return false;
        }

        foreach (var node in stmt.DescendantNodesAndSelf())
        {
            if (node is DeclarationExpressionSyntax or
                BreakStatementSyntax or
                ContinueStatementSyntax or
                ReturnStatementSyntax or
                GotoStatementSyntax or
                YieldStatementSyntax)
            {
                return false;
            }
        }

        return true;
    }
}
