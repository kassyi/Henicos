using Microsoft.CodeAnalysis.FlowAnalysis;
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

    // What a flow capture stands for: the values captured into it, one per branch (a conditional, a with expression).
    // Keyed by the capture id alone: a graph spliced into the analysis more than once (a local function body at each
    // call) adds its sources again, and an id that two graphs share would only join their sources (the safe side)
    private readonly Dictionary<CaptureId, ImmutableArray<IOperation>.Builder> _captureSources = new();

    // The captures being resolved, so that a capture that reaches itself through another one stops there
    private readonly HashSet<CaptureId> _capturesInProgress = new();

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

    /// <summary><paramref name="capture"/> holds <paramref name="source"/> on one of its branches.</summary>
    public void AddCaptureSource(CaptureId capture, IOperation source)
    {
        if (!_captureSources.TryGetValue(capture, out var sources))
        {
            sources = ImmutableArray.CreateBuilder<IOperation>();
            _captureSources.Add(capture, sources);
        }

        sources.Add(source);
    }

    /// <summary>What <paramref name="capture"/> holds, when anything was captured into it.</summary>
    public bool TryGetCaptureSources(CaptureId capture, out ImmutableArray<IOperation>.Builder sources) =>
        _captureSources.TryGetValue(capture, out sources);

    /// <summary>Starts resolving <paramref name="capture"/>; false when it is already being resolved.</summary>
    public bool EnterCapture(CaptureId capture) => _capturesInProgress.Add(capture);

    /// <summary>Ends resolving <paramref name="capture"/>.</summary>
    public void ExitCapture(CaptureId capture) => _capturesInProgress.Remove(capture);

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
