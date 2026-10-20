namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Connects all AST lowering passes into a pipeline that transforms standard C# kernel bodies into a flat state machine.</summary>
public static class AstLoweringPipeline
{
    /// <summary>Annotation kind that records the span of the original node a lowered node was derived from.</summary>
    public const string OriginalSpanAnnotationKind = "GSharpOriginalSpan";

    /// <summary>Lowers a kernel block into a flat state machine block and extracts hoisted context fields.</summary>
    public static (BlockSyntax LoweredBody, IReadOnlyList<VariableDeclaratorSyntax> ContextFields) Lower(
        BlockSyntax body)
    {
        var annotatedBody = (BlockSyntax)SpanAnnotationRewriter.Instance.Visit(body)!;

        var hoistedBody = new WarpInstructionRewriter().Rewrite(annotatedBody);
        hoistedBody = new BranchRewriter().Rewrite(hoistedBody);
        hoistedBody = new LoopRewriter().Rewrite(hoistedBody);

        var (contextHoistedBody, contextFields) = new ContextHoister().Hoist(hoistedBody);
        var stateMachineBody = new StateMachineLowerer().Lower(contextHoistedBody);

        return (stateMachineBody, contextFields);
    }

    private sealed class SpanAnnotationRewriter : CSharpSyntaxRewriter
    {
        public static readonly SpanAnnotationRewriter Instance = new();

        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return null;
            }

            var visited = base.Visit(node)!;
            return visited.WithAdditionalAnnotations(
                new SyntaxAnnotation(OriginalSpanAnnotationKind, $"{node.SpanStart},{node.Span.Length}"));
        }
    }
}

/// <summary>Provides extension methods for chaining syntax rewriter passes on a block.</summary>
internal static class BlockSyntaxLoweringExtensions
{
    public static BlockSyntax LowerWith<T>(this BlockSyntax block) where T : CSharpSyntaxRewriter, new() =>
        (BlockSyntax)new T().Visit(block);
}

