namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What <see cref="UniformityRecorder"/> records for one node of a <see cref="SplicedFlowGraph"/>.</summary>
internal sealed class UniformityNode
{
    public ImmutableArray<UniformityEvent>.Builder Events { get; } = ImmutableArray.CreateBuilder<UniformityEvent>();

    /// <summary>
    /// Whether the node calls something that splits the state machine (a barrier, a warp shuffle, a call that suspends).
    /// </summary>
    public bool HasSplit { get; set; }

    /// <summary>Those calls, in order.</summary>
    public ImmutableArray<SyntaxNode>.Builder Splits { get; } = ImmutableArray.CreateBuilder<SyntaxNode>();

    /// <summary>The values that the events of the node write.</summary>
    public ImmutableArray<int>.Builder Written { get; } = ImmutableArray.CreateBuilder<int>();
}
