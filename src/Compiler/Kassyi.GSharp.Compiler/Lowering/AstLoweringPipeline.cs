namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Connects all AST lowering passes into a pipeline that transforms standard C# kernel bodies into a flat state machine.</summary>
public static class AstLoweringPipeline
{
    /// <summary>Lowers a kernel block into a flat state machine block and extracts hoisted context fields.</summary>
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

/// <summary>Provides extension methods for chaining syntax rewriter passes on a block.</summary>
internal static class BlockSyntaxLoweringExtensions
{
    public static BlockSyntax LowerWith<T>(this BlockSyntax block) where T : CSharpSyntaxRewriter, new() =>
        (BlockSyntax)new T().Visit(block);
}

