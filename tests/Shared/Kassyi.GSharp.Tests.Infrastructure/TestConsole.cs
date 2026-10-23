#nullable enable
namespace Kassyi.GSharp.Tests.Infrastructure;

/// <summary>
/// Keeps test runs readable by discarding what tests and the code under test print to the console
/// (<c>Logger</c> output, benchmark tables, progress bars). xUnit v2 does not capture <see cref="Console"/> output,
/// so without this it is interleaved with the runner's own lines.
/// </summary>
/// <remarks>
/// The redirect is process-wide, so it also applies to xUnit worker threads. Set <c>GSHARP_TEST_VERBOSE=1</c> to keep the output.
/// </remarks>
public static class TestConsole
{
    private const string VerboseVariable = "GSHARP_TEST_VERBOSE";

    /// <summary>
    /// Redirects <see cref="Console.Out"/> and <see cref="Console.Error"/> to <see cref="TextWriter.Null"/>
    /// unless <c>GSHARP_TEST_VERBOSE</c> is <c>1</c>.
    /// </summary>
    public static void Silence()
    {
        if (Environment.GetEnvironmentVariable(VerboseVariable) == "1")
        {
            return;
        }

        Console.SetOut(TextWriter.Null);
        Console.SetError(TextWriter.Null);
    }
}
