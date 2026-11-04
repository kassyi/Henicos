namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>
/// Represents a CLI subcommand supported by <c>tools/fuzz-diff</c>.
/// </summary>
public interface IFuzzCommand
{
    /// <summary>Gets the subcommand token (for example, <c>gen</c>, <c>reduce</c>, <c>dedup</c>, or <c>promote</c>).</summary>
    string Name { get; }

    /// <summary>Parses command-line arguments and executes the subcommand.</summary>
    int Run(ReadOnlySpan<string> args);
}
