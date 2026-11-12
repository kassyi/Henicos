using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// Decides whether an operation is a reference that host code makes and C# runs, the common condition of the host rules
/// (GSH0018, GSH0031).
/// </summary>
internal static class HostReferences
{
    /// <summary>
    /// Whether <paramref name="operation"/> runs on the host: it is outside kernels and device functions (lambdas and
    /// local functions inside them included), outside G#'s own generated output, not the argument of <c>nameof</c>,
    /// and not in a call that C# removes (<c>[Conditional]</c> without its symbol, a partial method without a body).
    /// </summary>
    public static bool IsHostReference(IOperation operation, CancellationToken cancellationToken)
    {
        var syntax = operation.Syntax;
        if (GSharpGeneratedTrees.IsOwnOutput(syntax.SyntaxTree) || IsInNameOf(operation) ||
            operation.SemanticModel is not { } model)
        {
            return false;
        }

        return !GpuExecutionSpace.IsWithinGpuFunction(model.GetEnclosingSymbol(syntax.SpanStart, cancellationToken)) &&
               !RemovedCalls.IsInRemovedCall(syntax, model);
    }

    private static bool IsInNameOf(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is INameOfOperation)
            {
                return true;
            }
        }

        return false;
    }
}
