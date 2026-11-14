using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// The value indices that <see cref="UniformityRecorder"/> gives the locals, parameters, flow captures and local
/// function results that the graph writes. The memory continues the same numbering (<see cref="MemoryValues"/>).
/// </summary>
/// <param name="recorder">The recorder whose value count the next index is.</param>
internal sealed class UniformityValues(UniformityRecorder recorder)
{
    private readonly Dictionary<ISymbol, int> _symbols = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<(ControlFlowGraph Graph, CaptureId Id), int> _captures = new();

    // The flow captures whose members a write names, by the reference in that write
    private readonly Dictionary<IFlowCaptureReferenceOperation, int> _captureRoots = new();

    // The locals and parameters that a flow capture refers to by reference (each branch of a ref conditional)
    private readonly Dictionary<(ControlFlowGraph Graph, CaptureId Id), ImmutableArray<ISymbol>.Builder>
        _captureAliases = new();

    // The same, by the reference of a write that names a member of the capture
    private readonly Dictionary<IFlowCaptureReferenceOperation, ImmutableArray<ISymbol>.Builder> _rootAliases = new();

    // The result of each local function call whose body is spliced where it is called
    private readonly Dictionary<SyntaxNode, int> _results = new();

    /// <summary>The number of values: locals, parameters, flow captures and local function results.</summary>
    public int Count => _symbols.Count + _captures.Count + _results.Count;

    /// <summary>The value index of a local or parameter, when the graph writes it.</summary>
    public bool TryGetValue(ISymbol symbol, out int index) => _symbols.TryGetValue(symbol, out index);

    /// <summary>
    /// The value index of the result of a local function call, when its body is spliced where it is called.
    /// </summary>
    public bool TryGetResult(SyntaxNode call, out int index) => _results.TryGetValue(call, out index);

    /// <summary>The value index of a flow capture, when the graph writes it.</summary>
    public bool TryGetCapture(ControlFlowGraph graph, CaptureId id, out int index) =>
        _captures.TryGetValue((graph, id), out index);

    /// <summary>The value index of a flow capture, given one on first use.</summary>
    public int ValueOf(ControlFlowGraph graph, CaptureId id)
    {
        if (!_captures.TryGetValue((graph, id), out var index))
        {
            index = recorder.ValueCount;
            _captures.Add((graph, id), index);
        }

        return index;
    }

    /// <summary>
    /// The value index of the flow capture that <paramref name="reference"/> names as the struct a write changes, kept
    /// for the reference.
    /// </summary>
    public int CaptureRootOf(IFlowCaptureReferenceOperation reference, ControlFlowGraph graph)
    {
        var index = ValueOf(graph, reference.Id);
        _captureRoots[reference] = index;
        if (_captureAliases.TryGetValue((graph, reference.Id), out var aliases))
        {
            _rootAliases[reference] = aliases;
        }

        return index;
    }

    /// <summary>
    /// <paramref name="symbol"/> is what the flow capture <paramref name="id"/> refers to on one of its branches (a ref
    /// conditional), given a value index.
    /// </summary>
    public void AddCaptureAlias(ControlFlowGraph graph, CaptureId id, ISymbol symbol)
    {
        if (!_captureAliases.TryGetValue((graph, id), out var aliases))
        {
            aliases = ImmutableArray.CreateBuilder<ISymbol>();
            _captureAliases.Add((graph, id), aliases);
        }

        if (!aliases.Contains(symbol, SymbolEqualityComparer.Default))
        {
            aliases.Add(symbol);
        }

        ValueOf(symbol);
    }

    /// <summary>The locals and parameters that the capture <paramref name="reference"/> names refers to.</summary>
    public ImmutableArray<ISymbol> GetRootAliases(IFlowCaptureReferenceOperation reference) =>
        _rootAliases.TryGetValue(reference, out var aliases) ? aliases.ToImmutable() : ImmutableArray<ISymbol>.Empty;

    /// <summary>The value index that <see cref="CaptureRootOf"/> kept for <paramref name="reference"/>.</summary>
    public bool TryGetCaptureRoot(IFlowCaptureReferenceOperation reference, out int index) =>
        _captureRoots.TryGetValue(reference, out index);

    /// <summary>The value index of a local or parameter, given one on first use.</summary>
    public int ValueOf(ISymbol symbol)
    {
        if (!_symbols.TryGetValue(symbol, out var index))
        {
            index = recorder.ValueCount;
            _symbols.Add(symbol, index);
        }

        return index;
    }

    /// <summary>The value index of the result of a local function call, given one on first use.</summary>
    public int ResultOf(SyntaxNode call)
    {
        if (!_results.TryGetValue(call, out var index))
        {
            index = recorder.ValueCount;
            _results.Add(call, index);
        }

        return index;
    }
}
