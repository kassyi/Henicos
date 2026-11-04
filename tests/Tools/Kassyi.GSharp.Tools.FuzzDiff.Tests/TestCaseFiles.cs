#nullable enable
namespace Kassyi.GSharp.Tools.FuzzDiff.Tests;

/// <summary>Reads the input files under <c>TestCases/</c> of this test project, which are not compiled.</summary>
internal static class TestCaseFiles
{
    /// <summary>Reads <c>TestCases/{folder}/{fileName}</c>.</summary>
    internal static string Read(string folder, string fileName) =>
        File.ReadAllText(
            RepositoryPaths.Combine("tests", "Tools", "Kassyi.GSharp.Tools.FuzzDiff.Tests", "TestCases", folder, fileName));
}
