using Microsoft.CodeAnalysis.FlowAnalysis;

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
