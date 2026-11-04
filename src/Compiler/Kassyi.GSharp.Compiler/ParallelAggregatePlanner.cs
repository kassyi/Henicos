namespace Kassyi.GSharp.Compiler;

/// <summary>How the CUDA dialect evaluates one <c>AsParallel()</c> aggregate. The CPU dialects run it in each thread.</summary>
internal enum ParallelAggregateStrategy
{
    /// <summary>Each thread sums the whole sequence on its own.</summary>
    PerThread,

    /// <summary>The threads of a block share the elements and combine their partial sums (every block sums all of it).</summary>
    Block,

    /// <summary>The threads of the grid share the elements and combine through a grid sync (cooperative kernels).</summary>
    Grid
}

/// <summary>Why an aggregate runs in each thread (the reason of Warning A, GSH0099).</summary>
internal enum ParallelAggregateFallback
{
    None,
    OutsideKernelBody,
    InsideLambda,
    DivergentCall,
    EarlyExit,
    ThreadPrivateSource,
    OrderDependentStage,
    SideEffect,
    ThreadVariantInput,
    UnsupportedElementType,
    Unresolved
}

/// <summary>The strategy of one aggregate, why it fell back to each thread, and whether its inputs are one in the grid.</summary>
internal readonly record struct ParallelAggregatePlan(
    ParallelAggregateStrategy Strategy,
    ParallelAggregateFallback Fallback,
    bool IsGridUniform);

/// <summary>
/// Decides how the terminal call of a fused <c>AsParallel()</c> chain is evaluated (#624). The meaning is the one of C#:
/// every calling thread receives the sum of the whole sequence. The threads may share the work only when they all reach
/// the call together (a block barrier follows) and every thread would sum the same values; otherwise each thread sums on
/// its own and Warning A says why.
/// </summary>
/// <remarks>
/// [WHY] The decision is made on the tree of the compilation, so that the analyzer and both dialects agree. The variance
/// analysis is <see cref="UniformityAnalysis"/> (<see cref="ThreadVariance.Block"/>, and <see cref="ThreadVariance.Grid"/>
/// for <see cref="ParallelAggregatePlan.IsGridUniform"/>); when the body does not bind, the plan falls back.
/// </remarks>
internal static class ParallelAggregatePlanner
{
    /// <summary>The plan of the terminal call <paramref name="terminal"/> of a fused <c>AsParallel()</c> chain.</summary>
    public static ParallelAggregatePlan Plan(InvocationExpressionSyntax terminal, SemanticModel model)
    {
        if (model.GetOriginalNode(terminal) is not InvocationExpressionSyntax original ||
            !model.Compilation.ContainsSyntaxTree(original.SyntaxTree))
        {
            return PerThread(ParallelAggregateFallback.Unresolved);
        }

        var originalModel = model.Compilation.GetCachedSemanticModel(original.SyntaxTree);
        if (originalModel.GetSymbol(original) is not IMethodSymbol symbol ||
            !StackLinqChainExtractor.TryExtract(original, originalModel, symbol, out var chain))
        {
            return PerThread(ParallelAggregateFallback.Unresolved);
        }

        if (original.FirstAncestorOrSelf<MethodDeclarationSyntax>() is not { } method ||
            originalModel.GetDeclaredSymbol<IMethodSymbol>(method) is not { } kernel ||
            !kernel.HasAttribute(KnownAttributes.GpuKernel))
        {
            return PerThread(ParallelAggregateFallback.OutsideKernelBody);
        }

        if (IsInsideLambda(original, method, originalModel))
        {
            return PerThread(ParallelAggregateFallback.InsideLambda);
        }

        if (!new MethodFlowGraph(originalModel, method).TryGetUniformity(out var uniformity))
        {
            return PerThread(ParallelAggregateFallback.Unresolved);
        }

        // The first reason that applies, in the order of the arms
        var fallback = true switch
        {
            _ when uniformity.GetPartialReach(original) is var reach && reach != PartialReach.None =>
                reach == PartialReach.Branch
                    ? ParallelAggregateFallback.DivergentCall
                    : ParallelAggregateFallback.EarlyExit,
            _ when !IsSharedSource(chain.SourceArrayExpression, originalModel, kernel)
                => ParallelAggregateFallback.ThreadPrivateSource,
            _ when HasOrderDependentStage(in chain, originalModel) => ParallelAggregateFallback.OrderDependentStage,
            _ when HasSideEffect(in chain, originalModel) => ParallelAggregateFallback.SideEffect,
            _ when ReadsVariantInput(in chain, originalModel, kernel, uniformity, ThreadVariance.Block)
                => ParallelAggregateFallback.ThreadVariantInput,
            _ when symbol.ReturnType.SpecialType is not (SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_UInt64)
                => ParallelAggregateFallback.UnsupportedElementType,
            _ => ParallelAggregateFallback.None
        };
        if (fallback != ParallelAggregateFallback.None)
        {
            return PerThread(fallback);
        }

        var isGridUniform = !uniformity.IsReachedByPartOfGrid(original) &&
                            !ReadsVariantInput(in chain, originalModel, kernel, uniformity, ThreadVariance.Grid);
        var strategy = isGridUniform && kernel.IsCooperativeKernel()
            ? ParallelAggregateStrategy.Grid
            : ParallelAggregateStrategy.Block;
        return new ParallelAggregatePlan(strategy, ParallelAggregateFallback.None, isGridUniform);
    }

    /// <summary>The bytes of one slot of the grid scratch, aligned to 16.</summary>
    public const int GridScratchSlotBytes = 16;

    /// <summary>
    /// The bytes of one slot of the scratch that the grid strategy of the aggregates of <paramref name="method"/> needs
    /// (<see cref="GridScratchSlotBytes"/>), or 0 when no aggregate of the kernel uses the grid strategy. The CUDA
    /// launcher allocates one slot per block and one for the total, and passes it as the last kernel parameter.
    /// </summary>
    public static int GetGridScratchSlotBytes(MethodDeclarationSyntax method, SemanticModel model)
    {
        foreach (var node in method.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax invocation &&
                model.GetSymbol(invocation) is IMethodSymbol symbol &&
                StackLinqChainExtractor.TryExtract(invocation, model, symbol, out var chain) &&
                chain.IsParallel &&
                Plan(invocation, model).Strategy == ParallelAggregateStrategy.Grid)
            {
                return GridScratchSlotBytes;
            }
        }

        return 0;
    }

    /// <summary>The words that complete Warning A for <paramref name="fallback"/>: "runs sequentially ... because ...".</summary>
    public static string Describe(ParallelAggregateFallback fallback) => fallback switch
    {
        ParallelAggregateFallback.OutsideKernelBody => "it is in a device function, which every caller shares",
        ParallelAggregateFallback.InsideLambda => "it is inside a lambda or local function, which threads may run a different number of times",
        ParallelAggregateFallback.DivergentCall => "it is under a condition that may differ between the threads of a block",
        ParallelAggregateFallback.EarlyExit => "some threads may return, break or jump before reaching it",
        ParallelAggregateFallback.ThreadPrivateSource => "its source is not an array or span parameter of the kernel (each thread has its own)",
        ParallelAggregateFallback.OrderDependentStage => "a stage depends on the elements before it (TakeWhile, SkipWhile, or Skip / Take after Where)",
        ParallelAggregateFallback.SideEffect => "a lambda of the chain has a statement body or writes to a variable or memory",
        ParallelAggregateFallback.ThreadVariantInput => "its source, a stage argument or a value a lambda reads may differ between threads",
        ParallelAggregateFallback.UnsupportedElementType => "only int, uint, long and ulong sums are shared between threads",
        _ => "it could not be traced to the source of the kernel"
    };

    private static ParallelAggregatePlan PerThread(ParallelAggregateFallback fallback) =>
        new(ParallelAggregateStrategy.PerThread, fallback, IsGridUniform: false);

    // A lambda or local function around the call, other than the body of Gpu.SyncGrid(body) / Gpu.ForUnroll, which the
    // compiler expands where it is written
    private static bool IsInsideLambda(SyntaxNode call, MethodDeclarationSyntax method, SemanticModel model)
    {
        for (var node = call.Parent; node != null && node != method; node = node.Parent)
        {
            if (node is LocalFunctionStatementSyntax)
            {
                return true;
            }

            if (node is not AnonymousFunctionExpressionSyntax lambda)
            {
                continue;
            }

            if (lambda.Parent is not ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax macro } ||
                model.GetSymbol(macro) is not IMethodSymbol macroSymbol ||
                GpuMacroClassifier.Classify(macroSymbol).Kind is not (GpuMacroKind.SyncGrid or GpuMacroKind.ForUnroll))
            {
                return true;
            }
        }

        return false;
    }

    // An array or span parameter of the kernel, or a local that is only ever that parameter (Span<int> s = data;)
    private static bool IsSharedSource(ExpressionSyntax source, SemanticModel model, IMethodSymbol kernel)
    {
        var symbol = model.GetSymbol(source);
        if (symbol is IParameterSymbol parameter)
        {
            return parameter.ContainingSymbol.IsEqualTo(kernel) && parameter.RefKind == RefKind.None;
        }

        if (symbol is not ILocalSymbol local || source is not IdentifierNameSyntax ||
            !local.TryGetDeclaringSyntax<VariableDeclaratorSyntax>(out var declarator) ||
            declarator.Initializer?.Value is not IdentifierNameSyntax initial ||
            model.GetSymbol(initial) is not IParameterSymbol { RefKind: RefKind.None } aliased ||
            !aliased.ContainingSymbol.IsEqualTo(kernel))
        {
            return false;
        }

        // Never assigned again
        var scope = declarator.FirstAncestorOrSelf<BlockSyntax>();
        return scope == null || !scope.DescendantNodes()
            .AsStackEnumerable()
            .Any((Local: local, Model: model), static (node, s) =>
                node is AssignmentExpressionSyntax assignment &&
                s.Model.GetSymbol(assignment.Left).IsEqualTo(s.Local)
            );
    }

    // TakeWhile / SkipWhile, or a position stage that counts the elements a Where passed
    private static bool HasOrderDependentStage(in StackLinqChain chain, SemanticModel model)
    {
        var range = StackLinqChainExtractor.CountRangeStages(in chain, model);
        for (var i = 0; i < chain.Stages.Length - 1; i++)
        {
            var op = chain.Stages[i].Operator;
            if (op is StackLinqOperator.TakeWhile or StackLinqOperator.SkipWhile ||
                (i >= range && op.IsPositionStage()))
            {
                return true;
            }
        }

        return false;
    }

    // [WHY] The shared form calls each lambda once per element instead of once per element per thread; the result is the
    // same only when the lambdas are expressions that write nothing (the number of lambda calls is not part of the
    // promise of AsParallel(): docs/spec/en/03_bcl_api/linq/ParallelStackEnumerable.md, "In kernels")
    private static bool HasSideEffect(in StackLinqChain chain, SemanticModel model)
    {
        foreach (var stage in chain.Stages)
        {
            if (!StackLinqHelper.HasPredicateOrSelector(stage.Invocation, stage.Symbol) ||
                stage.Operator.IsPositionStage())
            {
                continue;
            }

            var argument = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol).Expression;
            if (argument is not LambdaExpressionSyntax { ExpressionBody: { } body } ||
                !ExpressionEffects.Of(body, model).Writes.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    // The source, the stage arguments and the outer values the lambdas read: each must be the same in every thread (in
    // the scope of the analysis), and a lambda may read only scalars or the array and span parameters of the kernel
    private static bool ReadsVariantInput(in StackLinqChain chain, SemanticModel model, IMethodSymbol kernel,
        UniformityAnalysis uniformity, ThreadVariance minVariance)
    {
        if (uniformity.GetVariance(chain.SourceArrayExpression) >= minVariance)
        {
            return true;
        }

        foreach (var stage in chain.Stages)
        {
            if (!StackLinqHelper.HasPredicateOrSelector(stage.Invocation, stage.Symbol))
            {
                continue;
            }

            var argument = StackLinqHelper.GetLambdaArgument(stage.Invocation, stage.Symbol).Expression;
            if (argument is not LambdaExpressionSyntax lambda)
            {
                if (uniformity.GetVariance(argument) >= minVariance)
                {
                    return true;
                }

                continue;
            }

            // The body with the lambda's own parameters fixed: ThreadIndex, BlockIndex (grid scope), variant locals and
            // device functions that return a variant value all count
            var parameters = model.GetSymbol(lambda) is IMethodSymbol lambdaSymbol
                ? lambdaSymbol.Parameters
                : ImmutableArray<IParameterSymbol>.Empty;
            if (uniformity.GetVariance(lambda.Body, parameters) >= minVariance)
            {
                return true;
            }

            foreach (var node in lambda.DescendantNodes())
            {
                if (node is not IdentifierNameSyntax identifier)
                {
                    continue;
                }

                var symbol = model.GetSymbol(identifier);
                if (symbol is not (ILocalSymbol or IParameterSymbol) || IsDeclaredInside(symbol, lambda))
                {
                    continue;
                }

                // A lambda may read only scalars and the array and span parameters of the kernel
                var type = symbol is ILocalSymbol local ? local.Type : ((IParameterSymbol)symbol).Type;
                var isKernelParameter = symbol is IParameterSymbol p && p.ContainingSymbol.IsEqualTo(kernel);
                if (!isKernelParameter && (type is IPointerTypeSymbol || type.IsArrayLikeType()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsDeclaredInside(ISymbol symbol, SyntaxNode lambda) =>
        symbol.DeclaringSyntaxReferences
            .AsStackEnumerable()
            .Any(lambda, static (reference, node) => node.Span.Contains(reference.Span) &&
                                                      reference.SyntaxTree == node.SyntaxTree);
}
