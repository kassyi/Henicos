namespace Kassyi.GSharp.Compiler.Ir.Linear;

/// <summary>A call to a G# state-machine runtime helper function.</summary>
/// <param name="function">The runtime helper called.</param>
/// <param name="shuffle">The warp shuffle kind for publish/read calls.</param>
/// <param name="site">The zero-based warp site index.</param>
/// <param name="arguments">The arguments passed to the helper.</param>
/// <param name="place">The formatted source place for publish/read/arrive calls, or <see langword="null"/>.</param>
/// <param name="type">The expression result type.</param>
/// <param name="source">Where the node comes from.</param>
internal sealed class IrRuntimeCall(IrRuntimeFunction function, WarpShuffleKind shuffle, int site,
    ImmutableArray<IrRuntimeArgument> arguments, string? place, ITypeSymbol type, IrSource source)
    : IrExpression(type, IrCategory.Value, default, source)
{
    /// <summary>The runtime helper called.</summary>
    public IrRuntimeFunction Function { get; } = function;

    /// <summary>The warp shuffle kind for publish/read calls.</summary>
    public WarpShuffleKind Shuffle { get; } = shuffle;

    /// <summary>The zero-based warp site index.</summary>
    public int Site { get; } = site;

    /// <summary>The arguments passed to the helper.</summary>
    public ImmutableArray<IrRuntimeArgument> Arguments { get; } = arguments;

    /// <summary>The formatted source place for publish/read/arrive calls, or <see langword="null"/>.</summary>
    public string? Place { get; } = place;

    /// <summary>This node with <paramref name="arguments"/>; this node itself when they are the same.</summary>
    public IrRuntimeCall Update(ImmutableArray<IrRuntimeArgument> arguments) =>
        arguments == Arguments
            ? this
            : KeepSpelling(new IrRuntimeCall(Function, Shuffle, Site, arguments, Place, Type, Source));
}
