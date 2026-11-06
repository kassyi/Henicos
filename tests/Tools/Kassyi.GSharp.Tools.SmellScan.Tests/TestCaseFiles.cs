#nullable enable
namespace Kassyi.GSharp.Tools.SmellScan.Tests;

/// <summary>Reads the input files under <c>TestCases/</c> of this test project, which are not compiled.</summary>
internal static class TestCaseFiles
{
    /// <summary>Gets the full path of <c>TestCases/{name}</c>.</summary>
    internal static string PathOf(string name) =>
        RepositoryPaths.Combine("tests", "Tools", "Kassyi.GSharp.Tools.SmellScan.Tests", "TestCases", name);

    /// <summary>Reads <c>TestCases/{name}</c>.</summary>
    internal static string Read(string name) => File.ReadAllText(PathOf(name));
}
