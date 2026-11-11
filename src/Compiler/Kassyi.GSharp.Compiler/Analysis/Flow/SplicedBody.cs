using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// A body that <see cref="SplicedFlowBuilder"/> splices into the graph. Every kind but
/// <see cref="SplicedBodyKind.Macro"/> is opaque: the state machine does not split it, and a lambda or a delegate may
/// run it at any later time.
/// </summary>
/// <param name="Kind">Why the body is where it is.</param>
/// <param name="Syntax">The lambda or the local function declaration.</param>
/// <param name="Parameters">The parameters of the lambda or local function.</param>
/// <param name="HasUniformIndex">Whether the last parameter is the index of a <c>Gpu.ForUnroll</c> body.</param>
/// <param name="Call">The call that runs a <see cref="SplicedBodyKind.LocalFunctionCall"/> body.</param>
/// <param name="CallGraph">The Roslyn graph of <paramref name="Call"/>.</param>
internal readonly record struct SplicedBody(
    SplicedBodyKind Kind,
    SyntaxNode Syntax,
    ImmutableArray<IParameterSymbol> Parameters,
    bool HasUniformIndex = false,
    IInvocationOperation? Call = null,
    Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph? CallGraph = null)
{
    /// <summary>Whether the body may run at another time than where it is spliced, outside the state machine's splits.</summary>
    public bool IsOpaque => Kind != SplicedBodyKind.Macro;
}
