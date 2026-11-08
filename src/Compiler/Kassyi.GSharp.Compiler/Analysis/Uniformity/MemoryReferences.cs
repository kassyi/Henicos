using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// What the locals that <see cref="MemoryUniformity"/> follows refer to, and the memories each location was resolved
/// to.
/// </summary>
internal sealed class MemoryReferences
{
    // What a local refers to: for a local of array, span or SharedMemory<T> type, the references assigned to it; for a
    // ref local, the locations it is bound to
    private readonly Dictionary<ILocalSymbol, ImmutableArray<IOperation>.Builder> _sources =
        new(SymbolEqualityComparer.Default);

    // The ref locals bound to a location this class does not follow (a pointer): they may reach any memory
    private readonly HashSet<ILocalSymbol> _unknownTargets = new(SymbolEqualityComparer.Default);

    // The memories each location reaches, resolved on first use (after recording, when every source is known)
    private readonly Dictionary<IOperation, ImmutableArray<int>> _resolved = new();

    /// <summary><paramref name="local"/> refers to <paramref name="source"/> (a reference or a location).</summary>
    public void AddSource(ILocalSymbol local, IOperation source)
    {
        if (!_sources.TryGetValue(local, out var sources))
        {
            sources = ImmutableArray.CreateBuilder<IOperation>();
            _sources.Add(local, sources);
        }

        sources.Add(source);
    }

    /// <summary>What <paramref name="local"/> refers to, when anything was assigned or bound to it.</summary>
    public bool TryGetSources(ILocalSymbol local, out ImmutableArray<IOperation>.Builder sources) =>
        _sources.TryGetValue(local, out sources);

    /// <summary>
    /// <paramref name="refLocal"/> is bound to a location that is not followed, so it may reach any memory.
    /// </summary>
    public void AddUnknownTarget(ILocalSymbol refLocal) => _unknownTargets.Add(refLocal);

    /// <summary>Whether <paramref name="refLocal"/> may reach any memory.</summary>
    public bool HasUnknownTarget(ILocalSymbol refLocal) => _unknownTargets.Contains(refLocal);

    /// <summary>The memories <paramref name="operation"/> was resolved to, when it was.</summary>
    public bool TryGetResolved(IOperation operation, out ImmutableArray<int> memories) =>
        _resolved.TryGetValue(operation, out memories);

    /// <summary>Keeps the memories <paramref name="operation"/> resolves to.</summary>
    public void AddResolved(IOperation operation, ImmutableArray<int> memories) => _resolved.Add(operation, memories);
}
