namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Connects all AST Lowering passes into a single pipeline.
/// This transforms standard C# kernel bodies into a flat state machine suitable for CPU emulation.
/// </summary>
public static class AstLoweringPipeline
{
    public static (BlockSyntax LoweredBody, IReadOnlyList<VariableDeclaratorSyntax> ContextFields) Lower(
        BlockSyntax body)
    {
        var annotatedBody = body.ReplaceNodes(body.DescendantNodesAndSelf(), (original, rewritten) =>
            rewritten.WithAdditionalAnnotations(new SyntaxAnnotation("GSharpOriginalSpan",
                $"{original.SpanStart},{original.Span.Length}"))
        );

        var hoistedBody = annotatedBody
            .LowerWith<WarpInstructionRewriter>()
            .LowerWith<BranchRewriter>()
            .LowerWith<LoopRewriter>();

        var (contextHoistedBody, contextFields) = new ContextHoister().Hoist(hoistedBody);
        var stateMachineBody = new StateMachineLowerer().Lower(contextHoistedBody);

        return (stateMachineBody, contextFields);
    }
}

internal static class BlockSyntaxLoweringExtensions
{
    public static BlockSyntax LowerWith<T>(this BlockSyntax block) where T : CSharpSyntaxRewriter, new() =>
        (BlockSyntax)new T().Visit(block);
}
