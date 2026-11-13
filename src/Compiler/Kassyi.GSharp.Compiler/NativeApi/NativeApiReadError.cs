namespace Kassyi.GSharp.Compiler.NativeApi;

internal sealed class NativeApiReadError(NativeApiReadErrorKind kind, int line, string message)
{
    public NativeApiReadErrorKind Kind { get; } = kind;

    /// <summary>Gets the 1-based line the error refers to.</summary>
    public int Line { get; } = line;

    public string Message { get; } = message;

    /// <summary>
    /// Gets the diagnostic ID this error is reported under. The build-time generator executable cannot link the
    /// generated <c>GSharpDiagnostics</c>, so the IDs are mirrored here and pinned to <c>diagnostics.json</c> by a test.
    /// </summary>
    public string DiagnosticId => Kind == NativeApiReadErrorKind.Syntax ? "GSH0052" : "GSH0053";
}
