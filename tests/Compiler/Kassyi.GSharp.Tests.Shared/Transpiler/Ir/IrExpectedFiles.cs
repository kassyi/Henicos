#nullable enable
namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// The expected files of the IR corpus tests (<c>&lt;name&gt;.expected.txt</c> next to the tests) and the received
/// files a run writes under <c>tmp/</c>, one per test assembly: Tests.Cpu and Tests.Cuda both compile these tests and
/// may run together.
/// </summary>
internal static class IrExpectedFiles
{
    private const string ExpectedSuffix = ".expected.txt";

    /// <summary>The lines sorted by ordinal, each ended by <c>\n</c>.</summary>
    public static string SortedLines(IEnumerable<string> lines) =>
        string.Join("\n", lines.OrderBy(static line => line, StringComparer.Ordinal)) + "\n";

    /// <summary>
    /// <c>tmp/&lt;name&gt;.&lt;test assembly&gt;.received.txt</c>, with its folder created.
    /// </summary>
    public static string ReceivedPath(string name)
    {
        var assembly = typeof(IrExpectedFiles).Assembly.GetName().Name;
        var path = RepositoryPaths.Combine("tmp", $"{name}.{assembly}.received.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        return path;
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> equals <paramref name="expectedFile"/> read with <c>\n</c> line ends;
    /// on a difference, writes <paramref name="actual"/> to the received file of the expected file, to compare with
    /// it and copy over it when the change is intended.
    /// </summary>
    public static void AssertMatches(string expectedFile, string actual, ITestOutputHelper output)
    {
        var expectedPath = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Transpiler",
            "Ir", expectedFile);
        var expected = File.Exists(expectedPath) ? string.Join("\n", File.ReadAllLines(expectedPath)) + "\n" : "";
        if (expected != actual)
        {
            var receivedPath = ReceivedPath(expectedFile[..^ExpectedSuffix.Length]);
            File.WriteAllText(receivedPath, actual);
            output.WriteLine("Received: " + receivedPath);
        }

        Assert.Equal(expected, actual);
    }
}
