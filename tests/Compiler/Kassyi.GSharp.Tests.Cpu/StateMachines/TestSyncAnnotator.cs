#nullable enable
using Kassyi.GSharp.Compiler.Lowering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kassyi.GSharp.Tests.Generators.StateMachines;

/// <summary>
/// Test only: puts the synchronization intrinsic annotation (<see cref="LoweringAnnotations.SyncIntrinsicAnnotationKind"/>)
/// on the barriers and warp shuffles of a block that has no semantic model, by their spelling.
/// </summary>
/// <remarks>
/// [WHY] The lowering recognizes a split point only by the annotation that <c>AstLoweringPipeline</c> puts on the
/// original tree from the symbol of the call (#503). The Tier 2 tests lower delegate bodies and parsed blocks that no
/// compilation binds, so they annotate the calls here instead: <c>SyncThreads()</c> and <c>SyncGrid(...)</c> with the
/// receiver <c>Gpu</c> or none, and <c>Warp.Shuffle*Sync</c>. Product code never guesses a barrier from its name.
/// </remarks>
internal static class TestSyncAnnotator
{
    public static TNode Annotate<TNode>(TNode node) where TNode : SyntaxNode =>
        (TNode)(new Rewriter().Visit(node) ?? node);

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = base.VisitInvocationExpression(node);
            if (visited == null || node.HasAnnotations(LoweringAnnotations.SyncIntrinsicAnnotationKind) ||
                Classify(node) is not { } intrinsic ||
                LoweringAnnotations.CreateSyncIntrinsicAnnotation(intrinsic) is not { } annotation)
            {
                return visited;
            }

            return visited.WithAdditionalAnnotations(annotation);
        }

        private static SyncIntrinsicInfo? Classify(InvocationExpressionSyntax node)
        {
            var (receiver, name) = node.Expression switch
            {
                MemberAccessExpressionSyntax memberAccess =>
                    ((memberAccess.Expression as IdentifierNameSyntax)?.Identifier.ValueText ?? "",
                        memberAccess.Name.Identifier.ValueText),
                IdentifierNameSyntax identifier => ("", identifier.Identifier.ValueText),
                _ => ("", "")
            };

            return (receiver, name) switch
            {
                ("Gpu" or "", "SyncThreads") => new SyncIntrinsicInfo(SyncIntrinsicKind.BlockBarrier, SyncScope.Block, true, false),
                ("Gpu" or "", "SyncGrid") => new SyncIntrinsicInfo(SyncIntrinsicKind.GridBarrier, SyncScope.Grid, true, false),
                ("Warp", "ShuffleSync") => Shuffle(WarpShuffleKind.Sync),
                ("Warp", "ShuffleDownSync") => Shuffle(WarpShuffleKind.Down),
                ("Warp", "ShuffleUpSync") => Shuffle(WarpShuffleKind.Up),
                ("Warp", "ShuffleXorSync") => Shuffle(WarpShuffleKind.Xor),
                _ => null
            };
        }

        private static SyncIntrinsicInfo Shuffle(WarpShuffleKind kind) =>
            new(SyncIntrinsicKind.WarpShuffle, SyncScope.Warp, true, true, kind);
    }
}
