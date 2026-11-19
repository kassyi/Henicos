using Kassyi.GSharp.Compiler.Aot;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Collects what one dialect's transpilation could not emit (<see cref="TranspileFailure"/>), each reason once, and
/// states the reasons in place of that dialect's output.
/// </summary>
internal sealed class FailureCollector
{
    private readonly ImmutableArray<string>.Builder _failures = ImmutableArray.CreateBuilder<string>();

    // The text is what the generated source states, so a failure found by both the signature and the body is one
    public void Fail(TranspileFailure failure)
    {
        var reason = failure.Format();
        if (!_failures.Contains(reason))
        {
            _failures.Add(reason);
        }
    }

    /// <summary>
    /// <paramref name="source"/> when nothing failed; otherwise one <c>#error</c> per reason, so that a failure the
    /// analyzer misses still stops the native build. The analyzer reports the same failures as diagnostics.
    /// </summary>
    public string Apply(string source) =>
        _failures.Count == 0 ? source : NativeModuleAssembler.FormatTranspileFailures(_failures);

    /// <summary>Whether anything failed.</summary>
    public bool HasFailures => _failures.Count > 0;
}
