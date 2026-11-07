using System.Runtime.ExceptionServices;
using System.Text;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Builds the <see cref="GpuOverflowException"/> of the checked signed operations, and captures the operator stack traces
/// when the <see cref="CaptureOperatorStackTracesSwitch"/> switch is set.
/// </summary>
/// <remarks>
/// Building a report reads the expression trees and formats text; it runs only when an overflow is thrown, so it is not
/// on the path of evaluations that do not overflow.
/// </remarks>
internal static class GpuOverflowReport
{
    /// <summary>
    /// The <see cref="AppContext"/> switch that makes each checked operator record the stack trace of the line that applied
    /// it, so that the exception points at that line.
    /// </summary>
    public const string CaptureOperatorStackTracesSwitch = "Kassyi.GSharp.GpuMath.CaptureOperatorStackTraces";

    /// <summary>
    /// Returns the stack trace of the caller of the operator that calls this method, or <see langword="null"/> unless the
    /// <see cref="CaptureOperatorStackTracesSwitch"/> switch is set. The operator must not be inlined, so that the frame
    /// to skip exists.
    /// </summary>
    /// <returns>The stack trace starting at the line that applied the operator.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static StackTrace? CaptureOperatorStackTrace() =>
        AppContext.TryGetSwitch(CaptureOperatorStackTracesSwitch, out var enabled) && enabled
            ? new StackTrace(2, true) // skips this method and the operator
            : null;

    /// <summary>Builds the exception of an overflow detected at once, on the host (CPU fallback mode).</summary>
    /// <param name="node">The overflowing node.</param>
    /// <param name="operation">Its operator.</param>
    /// <param name="resultWords">The number of words of its result.</param>
    /// <param name="operatorStackTrace">The stack trace of the operator, when captured.</param>
    /// <returns>The exception to throw.</returns>
    public static GpuOverflowException CreateImmediate(IGpuBigIntExpression node, string operation, int resultWords,
        StackTrace? operatorStackTrace)
    {
        var site = new OverflowSite(0, node, operation, resultWords, EvaluationSite.Current, operatorStackTrace);
        return Create([site.ToOrigin()], 0, detectedAtOnce: true);
    }

    /// <summary>Builds the exception that reports one or more overflows.</summary>
    /// <param name="origins">The overflows whose origins the GPU kept, oldest first.</param>
    /// <param name="overwrittenOrigins">The number of earlier overflows whose origins the GPU overwrote.</param>
    /// <param name="detectedAtOnce">
    /// <see langword="true"/> when the evaluation itself detected the overflow; <see langword="false"/> when a later
    /// synchronization of the workspace stream did.
    /// </param>
    /// <returns>The exception to throw.</returns>
    public static GpuOverflowException Create(IReadOnlyList<GpuOverflowOrigin> origins, int overwrittenOrigins,
        bool detectedAtOnce)
    {
        var message = new StringBuilder();
        var first = origins.Count > 0 ? origins[0] : GpuOverflowOrigin.Unknown;
        message.Append(first.IsKnown
            ? $"The {first}."
            : "A checked GpuBigIntExpr operation overflowed; its origin was overwritten by later operations.");
        message.Append(detectedAtOnce
            ? " Detected when it was evaluated (CPU fallback mode)."
            : " Detected at the first synchronization of the workspace stream after that evaluation.");
        message.Append(" A signed result keeps the width of its operands; give the operands more words.");
        if (origins.Count > 1)
        {
            message.Append($" {origins.Count - 1} more overflow(s) since the previous report are listed in Origins.");
        }

        if (overwrittenOrigins > 0)
        {
            message.Append($" {overwrittenOrigins} earlier overflow(s) were counted, ")
                .Append("but their origins were overwritten.");
        }

        var exception = new GpuOverflowException(message.ToString(), origins, overwrittenOrigins);
        if (first.OperatorStackTrace is { } stackTrace)
        {
            ExceptionDispatchInfo.SetRemoteStackTrace(exception, stackTrace);
        }

        return exception;
    }
}
