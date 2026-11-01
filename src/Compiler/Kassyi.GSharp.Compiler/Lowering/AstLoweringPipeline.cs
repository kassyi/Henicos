namespace Kassyi.GSharp.Compiler.Lowering;

/// <summary>Connects all AST lowering passes into a pipeline that transforms standard C# kernel bodies into a flat state machine.</summary>
public static class AstLoweringPipeline
{
    /// <summary>Lowers the body of a kernel or device function, block or expression body, into a flat state machine.</summary>
    /// <param name="method">A method that has a body (see <see cref="MethodDeclarationSyntaxExtensions.HasBody"/>).</param>
    /// <param name="semanticModel">A semantic model of the compilation, used to find the calls that split the state machine.</param>
    /// <param name="owner">
    /// The symbol of <paramref name="method"/>. The value parameters it writes live in the frame
    /// (<see cref="LoweredStateMachine.CopiedParameters"/>), because a kernel and a device function are both resumed
    /// with their original arguments.
    /// </param>
    /// <param name="isDeviceFunction">Whether <paramref name="method"/> is a device function rather than a kernel.</param>
    public static LoweredStateMachine Lower(MethodDeclarationSyntax method, SemanticModel? semanticModel,
        IMethodSymbol? owner, bool isDeviceFunction)
    {
        var body = method.GetBodyNode()
                   ?? throw new InvalidOperationException("A method without a body cannot be lowered into a state machine.");
        var returnsValue = method.ReturnType is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword };
        return Lower(body, returnsValue, semanticModel, owner, isDeviceFunction);
    }

    /// <summary>Lowers a kernel block into a flat state machine block and extracts hoisted context fields.</summary>
    /// <param name="body">The kernel or device function body.</param>
    /// <param name="semanticModel">
    /// A semantic model of the compilation, used to find the calls that split the state machine (barriers, warp shuffles,
    /// calls that suspend). Without it, only the calls that already carry a split annotation split the state machine.
    /// </param>
    /// <param name="owner">
    /// The kernel or device function that declares <paramref name="body"/>, whose written value parameters live in the
    /// frame, or <see langword="null"/> when there is none (a body built by a test), which copies no parameter.
    /// </param>
    /// <param name="isDeviceFunction">Whether <paramref name="body"/> is the body of a device function rather than a kernel.</param>
    public static LoweredStateMachine Lower(BlockSyntax body, SemanticModel? semanticModel, IMethodSymbol? owner,
        bool isDeviceFunction) =>
        Lower(body, returnsValue: false, semanticModel, owner, isDeviceFunction);

    // owner is the method that declares body: the device function, or the kernel when its symbol is known
    private static LoweredStateMachine Lower(CSharpSyntaxNode body, bool returnsValue, SemanticModel? semanticModel,
        IMethodSymbol? owner, bool isDeviceFunction)
    {
        var bodyModel = ResolveBodyModel(body, semanticModel);

        // [WHY] Annotate before wrapping an expression body: the annotations record spans of the original tree, which is
        // how later passes find the symbols of the synthesized block.
        var splitPoints = CollectSplitPoints(body, bodyModel);
        var treeOrdinal = SemanticModelExtensions.GetSyntaxTreeOrdinal(bodyModel?.Compilation, body.SyntaxTree);
        var annotatedBody = ToBlock(new SpanAnnotationRewriter(splitPoints, treeOrdinal).Visit(body) ?? body, returnsValue);
        annotatedBody = PreLoweringExpander.Expand(annotatedBody, bodyModel);
        annotatedBody = LabeledStatementSplitter.Split(annotatedBody);

        // [WHY] A kernel or a suspending device function is resumed with its original arguments on every state entry, so
        // every non-ref-struct by-value parameter of a split function lives in its frame (#879, #944, #992, #1000).
        var copiedParameters = owner != null
            ? FindCopiedValueParameters(owner)
            : ImmutableArray<string>.Empty;

        // [WHY] A loop header is evaluated at its own place in every iteration. The first round leaves headers alone;
        // once LoopRewriter has turned them into statements of the flat list, the second round lowers them there. Each
        // pass keeps one instance across both rounds so that temporaries and labels stay unique.
        var conditionalRewriter = new ConditionalEvaluationRewriter();
        var warpRewriter = new WarpInstructionRewriter(bodyModel);
        var branchRewriter = new BranchRewriter(bodyModel);
        var hoistedBody = conditionalRewriter.Rewrite(annotatedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);
        hoistedBody = new ForEachRewriter(bodyModel).Rewrite(hoistedBody);
        hoistedBody = new LoopRewriter().Rewrite(hoistedBody);
        hoistedBody = conditionalRewriter.Rewrite(hoistedBody);
        hoistedBody = warpRewriter.Rewrite(hoistedBody);
        hoistedBody = branchRewriter.Rewrite(hoistedBody);

        // [WHY] Found once, before ContextHoister rewrites declarations, and given to both passes so that they agree
        var statePlan = StateLabelPlanner.Plan(hoistedBody, bodyModel);
        var hoister = new ContextHoister(copiedParameters, bodyModel, owner, statePlan);
        var (contextHoistedBody, contextFields) = hoister.Hoist(hoistedBody);
        if (!hoister.HasSplitPoint)
        {
            // [WHY] A body without a split point runs once and reads its parameters where they are
            copiedParameters = ImmutableArray<string>.Empty;
        }

        var lowerer = new StateMachineLowerer(isDeviceFunction, bodyModel, statePlan);
        var stateMachineBody = lowerer.Lower(contextHoistedBody);

        return new LoweredStateMachine(stateMachineBody, contextFields, lowerer.CallSites, copiedParameters);
    }

    private static SemanticModel? ResolveBodyModel(SyntaxNode body, SemanticModel? semanticModel) =>
        semanticModel == null
            ? null
            : semanticModel.SyntaxTree == body.SyntaxTree ||
              !semanticModel.Compilation.ContainsSyntaxTree(body.SyntaxTree)
                ? semanticModel
                : semanticModel.Compilation.GetSemanticModel(body.SyntaxTree);

    private static BlockSyntax ToBlock(SyntaxNode body, bool returnsValue) => body switch
    {
        BlockSyntax block => block,
        ArrowExpressionClauseSyntax { Expression: var expression } => SyntaxFactory.Block(
                (returnsValue
                    ? (StatementSyntax)SyntaxFactory.ReturnStatement(expression)
                    : SyntaxFactory.ExpressionStatement(expression))
                .WithAdditionalAnnotations(body.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)))
            .WithAdditionalAnnotations(body.GetAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind)),
        _ => throw new InvalidOperationException($"Cannot lower a body of kind '{body.Kind()}'.")
    };

    // Maps the span of each call that splits the state machine to its annotation: a call to a device function that
    // suspends (whether its callee returns a value), or a synchronization intrinsic that splits the state machine,
    // decided by symbol however the call is spelled (#503). Lambda bodies are included: PreLoweringExpander decides with
    // the annotations whether a Gpu.ForUnroll body holds a split point. Without a semantic model nothing is annotated.
    private static Dictionary<TextSpan, SyntaxAnnotation> CollectSplitPoints(SyntaxNode body, SemanticModel? bodyModel)
    {
        var splitPoints = new Dictionary<TextSpan, SyntaxAnnotation>();
        if (bodyModel == null)
        {
            return splitPoints;
        }

        foreach (var node in body.DescendantNodes())
        {
            if (node is not InvocationExpressionSyntax invocation ||
                bodyModel.GetSymbol(invocation) is not IMethodSymbol callee)
            {
                continue;
            }

            if (SuspensionAnalysis.IsSuspendingFunction(callee, bodyModel.Compilation))
            {
                splitPoints[invocation.Span] = callee.ReturnsVoid
                    ? new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind)
                    : new SyntaxAnnotation(LoweringAnnotations.SuspendingCallAnnotationKind,
                        LoweringAnnotations.ValueReturningCallAnnotationData);
            }
            else if (LoweringAnnotations.CreateSyncIntrinsicAnnotation(SyncIntrinsicClassifier.Classify(callee)) is { } sync)
            {
                splitPoints[invocation.Span] = sync;
            }
        }

        return splitPoints;
    }

    // [WHY] The caller (the launcher, for a kernel) passes the original arguments again on every resume, so every
    // non-ref-struct by-value parameter of a function that splits lives in its frame (#879, #944, #992, #1000).
    private static ImmutableArray<string> FindCopiedValueParameters(IMethodSymbol method)
    {
        var copied = ImmutableArray.CreateBuilder<string>(method.Parameters.Length);
        method.Parameters
            .AsStackEnumerable()
            .Where(static parameter => parameter.RefKind == RefKind.None && !parameter.Type.IsRefLikeType)
            .Select(static parameter => parameter.Name)
            .AddTo(copied);
        return copied.ToImmutable();
    }

    private sealed class SpanAnnotationRewriter(Dictionary<TextSpan, SyntaxAnnotation> splitPoints, int treeOrdinal) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? Visit(SyntaxNode? node)
        {
            if (node == null)
            {
                return null;
            }

            var visited = base.Visit(node);
            if (!node.HasAnnotations(LoweringAnnotations.OriginalSpanAnnotationKind))
            {
                visited = visited.WithAdditionalAnnotations(
                    new SyntaxAnnotation(
                        LoweringAnnotations.OriginalSpanAnnotationKind,
                        LoweringAnnotations.FormatOriginalSpanData(node, treeOrdinal)));
            }

            return node is InvocationExpressionSyntax && splitPoints.TryGetValue(node.Span, out var splitPoint)
                ? visited.WithAdditionalAnnotations(splitPoint)
                : visited;
        }
    }
}

/// <summary>Provides extension methods for chaining syntax rewriter passes on a block.</summary>
internal static class BlockSyntaxLoweringExtensions
{
    public static BlockSyntax LowerWith<T>(this BlockSyntax block) where T : CSharpSyntaxRewriter, new() =>
        (BlockSyntax)new T().Visit(block);
}
