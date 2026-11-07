namespace Kassyi.GSharp.Stdlib.GpuMath;

/// <summary>
/// The exception that is thrown when the exact result of a signed GPU big integer operation does not fit in the width of
/// its result.
/// </summary>
/// <remarks>
/// <para>
/// A signed <see cref="BigNum.Expressions.GpuBigIntExpr"/> keeps the width of its operands: <c>a + b</c> on 1-word operands
/// has a 1-word result. When the exact value needs more words, the operation is flagged on the GPU without waiting for it,
/// and the flag is thrown as this exception at the next synchronization of the workspace stream: the read of a value
/// (<c>ToBigInteger</c>, <c>ToHostArray</c>), a comparison, <c>GpuBigIntExecutableGraph.Execute</c>, and so on. The
/// evaluation that overflowed has already returned by then, so <see cref="Origins"/> says where it was evaluated.
/// </para>
/// <para>
/// One exception reports every overflow counted on the workspace since the previous report, in the order the GPU ran
/// them; the message describes the first. In CPU fallback mode the evaluation itself throws.
/// </para>
/// <para>
/// By default an origin names the evaluation call (<c>Evaluate</c>, <c>CompileToGraph</c>, ...) by member, file and
/// line. When the <c>Kassyi.GSharp.GpuMath.CaptureOperatorStackTraces</c> switch is set
/// (<c>AppContext.SetSwitch</c> or the <c>RuntimeHostConfigurationOption</c> item in the project file) before the
/// expression is built, each checked operator also records the stack trace of the line that applied it, and the stack trace
/// of this exception starts with that of the first origin. The capture costs a few microseconds per operator.
/// </para>
/// </remarks>
public sealed class GpuOverflowException : OverflowException
{
    /// <summary>Initializes a new instance of the <see cref="GpuOverflowException"/> class.</summary>
    public GpuOverflowException()
    {
        Origins = [];
    }

    /// <summary>Initializes a new instance of the <see cref="GpuOverflowException"/> class with a message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public GpuOverflowException(string message)
        : base(message)
    {
        Origins = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuOverflowException"/> class with a message and an inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public GpuOverflowException(string message, Exception innerException)
        : base(message, innerException)
    {
        Origins = [];
    }

    internal GpuOverflowException(string message, IReadOnlyList<GpuOverflowOrigin> origins, int overwrittenOrigins)
        : base(message)
    {
        Origins = origins;
        OverwrittenOrigins = overwrittenOrigins;
    }

    /// <summary>Gets the overflows reported by this exception whose origins are known, oldest first.</summary>
    /// <remarks>
    /// The GPU keeps the origins of the latest eight overflows of a workspace; earlier ones are counted in
    /// <see cref="OverwrittenOrigins"/>. An origin whose record was overwritten on the host has
    /// <see cref="GpuOverflowOrigin.IsKnown"/> set to <see langword="false"/>.
    /// </remarks>
    public IReadOnlyList<GpuOverflowOrigin> Origins { get; }

    /// <summary>Gets the number of overflows reported by this exception whose origins were overwritten on the GPU.</summary>
    public int OverwrittenOrigins { get; }
}

/// <summary>
/// Describes one overflowing operation of a <see cref="GpuOverflowException"/>: the operator, the expression it belongs to
/// and the call that evaluated it.
/// </summary>
public sealed class GpuOverflowOrigin
{
    internal static readonly GpuOverflowOrigin Unknown = new();

    internal GpuOverflowOrigin()
    {
    }

    /// <summary>Gets a value indicating whether the origin was found; when not, every other member is empty.</summary>
    public bool IsKnown => Operation is not null;

    /// <summary>Gets the operator that overflowed (<c>+</c>, <c>-</c> or <c>~</c>).</summary>
    public string? Operation { get; internal init; }

    /// <summary>Gets the number of 32-bit words of the result that the exact value did not fit in.</summary>
    public int ResultWords { get; internal init; }

    /// <summary>
    /// Gets the operation as text, with its operands named <c>x0</c>, <c>x1</c>, ... in the order they appear in
    /// <see cref="Expression"/>.
    /// </summary>
    public string? Subexpression { get; internal init; }

    /// <summary>
    /// Gets the whole evaluated expression as text, with the overflowing operation in brackets (<c>[x0 + x1] - x2</c>), or
    /// <see langword="null"/> when the operation was not evaluated through a <c>GpuBigIntExpr</c> entry point.
    /// </summary>
    public string? Expression { get; internal init; }

    /// <summary>
    /// Gets the entry point that evaluated the expression (<c>Evaluate</c>, <c>CompileToGraph</c>,
    /// <c>EvaluateWithCudaGraph</c>, <c>EvaluateWithCudaGraphAsync</c>), or <see langword="null"/> when it is not known.
    /// </summary>
    /// <remarks>For a compiled graph, every replay reports the <c>CompileToGraph</c> call.</remarks>
    public string? EntryPoint { get; internal init; }

    /// <summary>Gets the member that called <see cref="EntryPoint"/>.</summary>
    public string? CallerMemberName { get; internal init; }

    /// <summary>Gets the source file of the call to <see cref="EntryPoint"/>.</summary>
    public string? CallerFilePath { get; internal init; }

    /// <summary>Gets the line of the call to <see cref="EntryPoint"/>, or 0 when it is not known.</summary>
    public int CallerLineNumber { get; internal init; }

    /// <summary>
    /// Gets the stack trace of the line that applied the operator, or <see langword="null"/> unless the
    /// <c>Kassyi.GSharp.GpuMath.CaptureOperatorStackTraces</c> switch was set when the expression was built.
    /// </summary>
    public string? OperatorStackTrace { get; internal init; }

    /// <summary>Describes the origin in one line.</summary>
    /// <returns>The operator, the expression and the evaluation call.</returns>
    public override string ToString()
    {
        if (!IsKnown)
        {
            return "an overflow whose origin was overwritten by later operations";
        }

        var text = $"`{Operation}` result does not fit in {ResultWords} word(s): `{Subexpression}`";
        if (Expression is null)
        {
            return text + " (call site not recorded: evaluated without a GpuBigIntExpr entry point)";
        }

        text += $" in the expression `{Expression}`, evaluated by {EntryPoint}";
        return CallerLineNumber == 0
            ? text
            : $"{text} at {CallerMemberName} in {CallerFilePath}:line {CallerLineNumber}";
    }
}
