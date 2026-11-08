namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>
/// Expands a <c>Gpu.GridStrideLoop</c> whose body splits the state machine into a standard <c>for</c> loop
/// (<see cref="PreLoweringExpander"/>).
/// </summary>
/// <remarks>
/// A grid-stride loop whose body splits the state machine (a warp shuffle, vote or reduction; a barrier there is
/// GSH0037) becomes a for loop here, as ForUnroll does, so that the state machine splits its body like any loop body
/// (#1091). It is the loop that GridStrideLoopMacroRule and the managed rewrite write, through the same prelude
/// helpers: the count and the step are read once before the loop, and the unsigned index starts and steps at values
/// that saturate at 2^31 (#712). The body is moved, not copied, so it keeps its original-span annotations and needs
/// no region.
/// </remarks>
internal static class GridStrideLoopExpansion
{
    // {
    //     uint __gsharp_lgsl_n_<id> = __gsharp_grid_stride_count(<count>);
    //     uint __gsharp_lgsl_s_<id> = __gsharp_grid_stride_step();
    //     for (uint __gsharp_lgsl_<id> = __gsharp_grid_stride_start(); __gsharp_lgsl_<id> < __gsharp_lgsl_n_<id>;
    //          __gsharp_lgsl_<id> += __gsharp_lgsl_s_<id>)
    //     {
    //         int __gsharp_unroll_<id> = __gsharp_grid_stride_index(__gsharp_lgsl_<id>);
    //         <body>
    //     }
    // }
    // or null when the call stays as written (its body holds no split point)
    public static BlockSyntax? TryExpand(
        ExpressionStatementSyntax node,
        InvocationExpressionSyntax invocation,
        GpuMacroCall call,
        SemanticModel? semanticModel,
        ref int expansionCounter)
    {
        var args = invocation.ArgumentList.Arguments;
        if (!MacroExpansionSyntax.TryGetLoopLambda(call, args, out var loop) ||
            call.GetArgument(args, call.ValueOrdinal)?.Expression is not { } countArgument)
        {
            return null;
        }

        var names = LoopNames.For(expansionCounter++);
        var annotations = MacroExpansionSyntax.GetOriginalSpans(node);
        var body = MacroExpansionSyntax.SubstituteBody(loop, semanticModel, names.Index, annotations);

        var countCall = MacroExpansionSyntax.CallPrelude(
            GSharpInternalNames.Prelude.GridStrideCount,
            countArgument.WithoutTrivia()
        );
        var countDeclaration = MacroExpansionSyntax.DeclareLocal(
            SyntaxKind.UIntKeyword,
            names.Count,
            countCall,
            annotations
        );
        var stepCall = MacroExpansionSyntax.CallPrelude(GSharpInternalNames.Prelude.GridStrideStep);
        var stepDeclaration = MacroExpansionSyntax.DeclareLocal(
            SyntaxKind.UIntKeyword,
            names.Step,
            stepCall,
            annotations
        );

        return SyntaxFactory.Block(countDeclaration, stepDeclaration, CreateLoop(names, body, annotations))
            .WithAdditionalAnnotations(annotations);
    }

    // for (uint __gsharp_lgsl_<id> = __gsharp_grid_stride_start(); __gsharp_lgsl_<id> < __gsharp_lgsl_n_<id>;
    //      __gsharp_lgsl_<id> += __gsharp_lgsl_s_<id>) { int __gsharp_unroll_<id> = <index>; <body> }
    private static ForStatementSyntax CreateLoop(
        LoopNames names,
        BlockSyntax body,
        ImmutableArray<SyntaxAnnotation> annotations)
    {
        // uint __gsharp_lgsl_<id> = __gsharp_grid_stride_start()
        var startDeclaration = MacroExpansionSyntax.DeclareVariable(
            SyntaxKind.UIntKeyword,
            names.UnsignedIndex,
            MacroExpansionSyntax.CallPrelude(GSharpInternalNames.Prelude.GridStrideStart),
            annotations
        );

        // __gsharp_lgsl_<id> < __gsharp_lgsl_n_<id>
        var condition = SyntaxFactory.BinaryExpression(
            SyntaxKind.LessThanExpression,
            SyntaxFactory.IdentifierName(names.UnsignedIndex),
            SyntaxFactory.IdentifierName(names.Count)
        ).WithAdditionalAnnotations(annotations);

        // __gsharp_lgsl_<id> += __gsharp_lgsl_s_<id>
        var incrementor = SyntaxFactory.AssignmentExpression(
            SyntaxKind.AddAssignmentExpression,
            SyntaxFactory.IdentifierName(names.UnsignedIndex),
            SyntaxFactory.IdentifierName(names.Step)
        ).WithAdditionalAnnotations(annotations);

        return SyntaxFactory.ForStatement(
            startDeclaration,
            SyntaxFactory.SeparatedList<ExpressionSyntax>(),
            condition,
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(incrementor),
            PrependIndex(names, body, annotations)
        ).WithAdditionalAnnotations(annotations);
    }

    // { int __gsharp_unroll_<id> = __gsharp_grid_stride_index(__gsharp_lgsl_<id>); <body statements> }
    // (a helper instead of a cast: a cast that the lowering builds has no type the emitters can resolve)
    private static BlockSyntax PrependIndex(
        LoopNames names,
        BlockSyntax body,
        ImmutableArray<SyntaxAnnotation> annotations)
    {
        var indexValue = MacroExpansionSyntax.CallPrelude(
            GSharpInternalNames.Prelude.GridStrideIndex,
            SyntaxFactory.IdentifierName(names.UnsignedIndex)
        );
        var indexDeclaration = MacroExpansionSyntax.DeclareLocal(
            SyntaxKind.IntKeyword,
            names.Index,
            indexValue,
            annotations
        );
        return SyntaxFactory.Block(body.Statements.Insert(0, indexDeclaration))
            .WithAdditionalAnnotations(annotations);
    }

    // The names of expansion <id>: the int loop variable, the unsigned index, the count and the step
    private readonly record struct LoopNames(string Index, string UnsignedIndex, string Count, string Step)
    {
        public static LoopNames For(int id) => new(
            $"{GSharpInternalNames.Lowering.UnrollIndexPrefix}{id}",
            $"{GSharpInternalNames.Lowering.LoweredGridStrideIndexPrefix}{id}",
            $"{GSharpInternalNames.Lowering.LoweredGridStrideCountPrefix}{id}",
            $"{GSharpInternalNames.Lowering.LoweredGridStrideStepPrefix}{id}"
        );
    }
}
