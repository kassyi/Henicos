namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The memories that <see cref="MemoryUniformity"/> follows, each a value of the analysis, and what a barrier and the
/// exit of the method read from them.
/// </summary>
/// <param name="recorder">The recorder whose value indices the memories continue.</param>
internal sealed class MemoryValues(UniformityRecorder recorder)
{
    // The memories, each a value of the analysis: a parameter, field or property of array, span or SharedMemory<T>
    // type, a SharedMemory<T> local that is not a copy of another, and a ref or out parameter; and the method itself
    // for "a barrier on every path from the start" (OnStart)
    private readonly Dictionary<ISymbol, int> _memory = new(SymbolEqualityComparer.Default);

    // The value indices of the memories named by a field or property, which a struct argument of a call may reach
    private readonly HashSet<int> _fieldMemory = [];

    // The value indices of SharedMemory<T> memories, which are never the same across blocks
    private readonly HashSet<int> _sharedMemory = [];

    private ImmutableArray<int> _allMemory;

    // The value index of "a barrier on every path from the start"
    private int _barrierOnEveryPath = -1;

    /// <summary>The number of values the memory takes.</summary>
    public int Count => _memory.Count;

    /// <summary>
    /// Defines the value that says whether a barrier lies on every path from the start of <paramref name="method"/>.
    /// </summary>
    public int DefineBarrierOnEveryPath(IMethodSymbol method)
    {
        _barrierOnEveryPath = MemoryOf(method);
        return _barrierOnEveryPath;
    }

    /// <summary>The value index of the memory <paramref name="symbol"/> names, given one on first use.</summary>
    public int MemoryOf(ISymbol symbol)
    {
        if (!_memory.TryGetValue(symbol, out var index))
        {
            index = recorder.ValueCount;
            _memory.Add(symbol, index);
            if (symbol is IFieldSymbol or IPropertySymbol)
            {
                _fieldMemory.Add(index);
            }

            if (MemoryOperations.GetMemoryType(symbol).IsSharedMemoryType())
            {
                _sharedMemory.Add(index);
            }
        }

        return index;
    }

    /// <summary>The value index of the memory <paramref name="symbol"/> names, when it has one.</summary>
    public bool TryGetIndex(ISymbol symbol, out int index) => _memory.TryGetValue(symbol, out index);

    /// <summary>
    /// Whether the memory <paramref name="index"/> is a <c>SharedMemory&lt;T&gt;</c>, never the same across blocks.
    /// </summary>
    public bool IsShared(int index) => _sharedMemory.Contains(index);

    /// <summary>Every memory but the barrier value.</summary>
    public ImmutableArray<int> GetAllMemory()
    {
        if (_allMemory.IsDefault)
        {
            var all = ImmutableArray.CreateBuilder<int>(_memory.Count);
            foreach (var index in _memory.Values)
            {
                if (index != _barrierOnEveryPath)
                {
                    all.Add(index);
                }
            }

            _allMemory = all.ToImmutable();
        }

        return _allMemory;
    }

    /// <summary>
    /// A barrier: every memory written since the last one is left at most <paramref name="left"/>. A block barrier
    /// leaves at most the difference between blocks (the other blocks may still be writing); a grid barrier none.
    /// </summary>
    public void Sync(ThreadVariance left, ThreadVariance[] current)
    {
        foreach (var memory in _memory.Values)
        {
            if (current[memory] != ThreadVariance.Uniform)
            {
                current[memory] = left;
            }
        }
    }

    /// <summary>
    /// How far the memory of each parameter of <paramref name="method"/>, and of the fields, may differ where it
    /// returns, and the barrier that every path to its return passes, from the state <paramref name="exit"/> at its
    /// exit.
    /// </summary>
    public (ImmutableArray<ThreadVariance> Parameters, ThreadVariance Fields, ThreadVariance BarrierOnEveryPath)
        Summarize(IMethodSymbol method, ThreadVariance[] exit)
    {
        var parameters = ImmutableArray.CreateBuilder<ThreadVariance>(method.Parameters.Length);
        foreach (var parameter in method.Parameters)
        {
            parameters.Add(_memory.TryGetValue(parameter, out var index) ? exit[index] : ThreadVariance.Uniform);
        }

        var fields = ThreadVariance.Uniform;
        foreach (var field in _fieldMemory)
        {
            fields |= exit[field];
        }

        var barrier = _barrierOnEveryPath >= 0 ? exit[_barrierOnEveryPath] : ThreadVariance.Block;
        return (parameters.MoveToImmutable(), fields, barrier);
    }
}
