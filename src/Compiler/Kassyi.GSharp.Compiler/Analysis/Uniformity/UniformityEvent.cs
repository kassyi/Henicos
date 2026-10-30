using Microsoft.CodeAnalysis.FlowAnalysis;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>What an operation of a node does for <see cref="UniformityAnalysis"/>.</summary>
/// <param name="Kind">What happens.</param>
/// <param name="Operation">The operation (none for <see cref="UniformityEventKind.Define"/>).</param>
/// <param name="Graph">The Roslyn graph of <paramref name="Operation"/>.</param>
/// <param name="Opaque">The body of the lambda or local function that the operation is in, when it may run at any time.</param>
/// <param name="Value">The value that <see cref="UniformityEventKind.Define"/> defines.</param>
/// <param name="Variance">The variance it defines it with.</param>
internal readonly record struct UniformityEvent(
    UniformityEventKind Kind,
    IOperation? Operation,
    ControlFlowGraph? Graph,
    SyntaxNode? Opaque,
    int Value = -1,
    ThreadVariance Variance = ThreadVariance.Uniform);
