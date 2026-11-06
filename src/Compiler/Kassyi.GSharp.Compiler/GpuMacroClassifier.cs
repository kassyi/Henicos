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
/// <remarks>
/// The ordinals are those of the overload's parameters, not positions in a call: a call may name its arguments in any
/// order, so an argument is found through <see cref="GetArgument"/>, never by indexing the argument list.
/// </remarks>
internal readonly struct GpuMacroCall(
    GpuMacroKind kind,
    IMethodSymbol? method = null,
    bool hasContext = false,
    int bodyOrdinal = -1)
{
    public GpuMacroKind Kind { get; } = kind;

    /// <summary>The overload; <c>null</c> for a call classified without a semantic model.</summary>
    public IMethodSymbol? Method { get; } = method;

    /// <summary>Whether the overload takes <c>ref TContext ctx</c> before its other arguments (ForUnroll / GridStrideLoop).</summary>
    public bool HasContext { get; } = hasContext;

    /// <summary>The ordinal of the first parameter after the context parameter (<c>start</c> or <c>maxBounds</c>).</summary>
    public int ValueOrdinal => HasContext ? 1 : 0;

    /// <summary>The ordinal of the body parameter; meaningful only when <see cref="HasBody"/> is set.</summary>
    public int BodyOrdinal { get; } = bodyOrdinal;

    /// <summary>Whether the overload takes a body lambda (SyncGrid has a form without one).</summary>
    public bool HasBody { get; } = bodyOrdinal >= 0;

    /// <summary>
    /// The argument of the call that the parameter at <paramref name="ordinal"/> receives, or <c>null</c> when the call
    /// has none. Without <see cref="Method"/> only a call without named arguments is read, by position.
    /// </summary>
    public ArgumentSyntax? GetArgument(SeparatedSyntaxList<ArgumentSyntax> arguments, int ordinal)
    {
        if (Method is { } method)
        {
            return ordinal < method.Parameters.Length
                ? ArgumentMatchingHelper.FindArgument(arguments, method.Parameters[ordinal])
                : null;
        }

        var hasName = arguments
            .AsStackEnumerable()
            .Any(static argument => argument.NameColon is not null);
        return !hasName && ordinal < arguments.Count ? arguments[ordinal] : null;
    }

    /// <summary>The body lambda of the call, or <c>null</c> when the body is not written as a lambda.</summary>
    public LambdaExpressionSyntax? GetBody(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        HasBody ? GetArgument(arguments, BodyOrdinal)?.Expression as LambdaExpressionSyntax : null;

    /// <summary>The context argument (<c>ref ctx</c>) of the call, or <c>null</c> for an overload without one.</summary>
    public ExpressionSyntax? GetContext(SeparatedSyntaxList<ArgumentSyntax> arguments) =>
        HasContext ? GetArgument(arguments, 0)?.Expression : null;
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
            return symbol.Name == TypeConstants.GpuMacros.Unroll ? new GpuMacroCall(GpuMacroKind.HintUnroll, symbol) : default;
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
                ? new GpuMacroCall(GpuMacroKind.ForUnroll, symbol, hasContext: true, bodyOrdinal: 3)
                : new GpuMacroCall(GpuMacroKind.ForUnroll, symbol, bodyOrdinal: 2),
            // GridStrideLoop(maxBounds, body) / GridStrideLoop(ref ctx, maxBounds, body)
            TypeConstants.GpuMacros.GridStrideLoop => parameterCount == 3
                ? new GpuMacroCall(GpuMacroKind.GridStrideLoop, symbol, hasContext: true, bodyOrdinal: 2)
                : new GpuMacroCall(GpuMacroKind.GridStrideLoop, symbol, bodyOrdinal: 1),
            TypeConstants.GpuMacros.SingleThread => new GpuMacroCall(GpuMacroKind.SingleThread, symbol, bodyOrdinal: 0),
            // SyncGrid(body) / SyncGrid()
            TypeConstants.GpuMacros.SyncGrid => parameterCount == 1
                ? new GpuMacroCall(GpuMacroKind.SyncGrid, symbol, bodyOrdinal: 0)
                : new GpuMacroCall(GpuMacroKind.SyncGrid, symbol),
            TypeConstants.GpuMacros.InlinePtx => new GpuMacroCall(GpuMacroKind.InlinePtx, symbol),
            TypeConstants.GpuMacros.Constant => new GpuMacroCall(GpuMacroKind.Constant, symbol),
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
        return call.HasBody && call.GetArgument(list.Arguments, call.BodyOrdinal) == argument ? call : default;
    }
}
