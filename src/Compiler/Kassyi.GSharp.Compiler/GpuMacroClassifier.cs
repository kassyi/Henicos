namespace Kassyi.GSharp.Compiler;

/// <summary>The <c>Gpu</c> macros that the transpiler expands and the analyzers validate.</summary>
internal enum GpuMacroKind
{
    None,
    ForUnroll,
    GridStrideLoop,
    SingleThread,
    SyncGrid,
    InlinePtx,
    Constant,
    HintUnroll
}

/// <summary>A <c>Gpu</c> macro and the overload a call resolved to.</summary>
internal readonly struct GpuMacroCall(GpuMacroKind kind, bool hasContext = false, int lambdaArgumentIndex = -1)
{
    public GpuMacroKind Kind { get; } = kind;

    /// <summary>Whether the overload takes <c>ref TContext ctx</c> before its other arguments (ForUnroll / GridStrideLoop).</summary>
    public bool HasContext { get; } = hasContext;

    /// <summary>The index of the first argument after the context argument.</summary>
    public int ArgumentOffset => HasContext ? 1 : 0;

    /// <summary>The index of the argument that holds the body lambda; meaningful only when <see cref="HasBody"/> is set.</summary>
    public int LambdaArgumentIndex { get; } = lambdaArgumentIndex;

    /// <summary>Whether the overload takes a body lambda (SyncGrid has a form without one).</summary>
    public bool HasBody { get; } = lambdaArgumentIndex >= 0;
}

/// <summary>
/// Identifies which <c>Kassyi.GSharp.Core.Gpu</c> macro, and which of its overloads, a method symbol is, so that the
/// transpiler rules and the analyzers share one definition of the macro signatures.
/// </summary>
internal static class GpuMacroClassifier
{
    public static GpuMacroCall Classify(IMethodSymbol symbol)
    {
        // A user's own type that is also named Gpu is not the macro host
        if (!symbol.ContainingNamespace.IsCoreNamespace())
        {
            return default;
        }

        var containingType = symbol.ContainingType;
        if (containingType is { Name: TypeConstants.GpuMacros.Hint, ContainingType.Name: TypeConstants.GpuType })
        {
            return symbol.Name == TypeConstants.GpuMacros.Unroll ? new GpuMacroCall(GpuMacroKind.HintUnroll) : default;
        }

        if (containingType?.Name != TypeConstants.GpuType)
        {
            return default;
        }

        var parameterCount = symbol.Parameters.Length;
        return symbol.Name switch
        {
            // ForUnroll(start, count, body) / ForUnroll(ref ctx, start, count, body)
            TypeConstants.GpuMacros.ForUnroll => parameterCount == 4
                ? new GpuMacroCall(GpuMacroKind.ForUnroll, hasContext: true, lambdaArgumentIndex: 3)
                : new GpuMacroCall(GpuMacroKind.ForUnroll, lambdaArgumentIndex: 2),
            // GridStrideLoop(maxBounds, body) / GridStrideLoop(ref ctx, maxBounds, body)
            TypeConstants.GpuMacros.GridStrideLoop => parameterCount == 3
                ? new GpuMacroCall(GpuMacroKind.GridStrideLoop, hasContext: true, lambdaArgumentIndex: 2)
                : new GpuMacroCall(GpuMacroKind.GridStrideLoop, lambdaArgumentIndex: 1),
            TypeConstants.GpuMacros.SingleThread => new GpuMacroCall(GpuMacroKind.SingleThread, lambdaArgumentIndex: 0),
            // SyncGrid(body) / SyncGrid()
            TypeConstants.GpuMacros.SyncGrid => parameterCount == 1
                ? new GpuMacroCall(GpuMacroKind.SyncGrid, lambdaArgumentIndex: 0)
                : new GpuMacroCall(GpuMacroKind.SyncGrid),
            TypeConstants.GpuMacros.InlinePtx => new GpuMacroCall(GpuMacroKind.InlinePtx),
            TypeConstants.GpuMacros.Constant => new GpuMacroCall(GpuMacroKind.Constant),
            _ => default
        };
    }

    /// <summary>
    /// The macro whose body <paramref name="lambda"/> is (the lambda written as its body argument), or the default call
    /// (<see cref="GpuMacroKind.None"/>) for any other lambda. A body runs within the call; it is never stored.
    /// </summary>
    public static GpuMacroCall ClassifyBody(SyntaxNode lambda, SemanticModel semanticModel)
    {
        if (lambda.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax macro } list } argument ||
            semanticModel.GetSymbol(macro) is not IMethodSymbol symbol)
        {
            return default;
        }

        var call = Classify(symbol);
        return call.HasBody && list.Arguments.IndexOf(argument) == call.LambdaArgumentIndex ? call : default;
    }
}
