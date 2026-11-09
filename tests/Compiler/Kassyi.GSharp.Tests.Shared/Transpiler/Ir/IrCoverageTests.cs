#nullable enable
using System.Diagnostics;

namespace Kassyi.GSharp.Tests.Shared.Transpiler.Ir;

/// <summary>
/// Builds the IR of every GPU method of the generator snapshot inputs (<c>TestCases/</c>) and of the differential
/// kernels (#1561): the builder never throws, every tree keeps the invariants, and what stays opaque, counted by
/// reason, operation kind and syntax kind, matches <c>IrOpaqueCoverage.expected.txt</c>. A change that makes more
/// constructs opaque fails; one that builds more of them updates the table, which shows how far the IR reaches.
/// </summary>
[Category(TestCategory.Compiler)]
public class IrCoverageTests(ITestOutputHelper output)
{
    private const string ExpectedFile = "IrOpaqueCoverage.expected.txt";

#pragma warning disable RS0030 // The test case files are parsed as the snapshot generator parses them
    private static readonly SyntaxTree s_globalUsings = CSharpSyntaxTree.ParseText(
        "global using System;" +
        "global using System.Collections.Generic;" +
        "global using System.IO;" +
        "global using System.Linq;" +
        "global using System.Runtime.CompilerServices;" +
        "global using System.Runtime.InteropServices;" +
        "global using Kassyi.GSharp.Core;",
        new CSharpParseOptions(LanguageVersion.Preview),
        path: "__GlobalUsings.g.cs");
#pragma warning restore RS0030

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_EveryGpuMethod_BuildsAndKeepsTheInvariants()
    {
        var stopwatch = Stopwatch.StartNew();
        var opaque = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var violations = new ConcurrentBag<string>();
        var built = 0;

        var testCases = Directory.GetFiles(
                RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "TestCases"), "*.cs",
                SearchOption.AllDirectories)
            .Where(static path => !path.EndsWith(".verified.cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        Parallel.ForEach(testCases, path =>
        {
#pragma warning disable RS0030 // One compilation per file, as the snapshot generator compiles them
            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path),
                new CSharpParseOptions(LanguageVersion.Preview), path: path);
#pragma warning restore RS0030
            var compilation = MegaCompilationCache.BaseCompilation.AddSyntaxTrees(s_globalUsings, tree);
            Interlocked.Add(ref built, IrCoverage.BuildAll(compilation, [tree], opaque, violations));
        });

        var shared = MegaCompilationCache.Compilation;
        var differential = shared.SyntaxTrees
            .Where(static tree => tree.FilePath.Contains(Path.DirectorySeparatorChar + "Differential" +
                                                         Path.DirectorySeparatorChar) &&
                                  tree.FilePath.EndsWith("Kernels.cu.cs", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(differential);
        built += IrCoverage.BuildAll(shared, differential, opaque, violations);

        output.WriteLine($"{testCases.Length} test case files, {differential.Length} differential files, " +
                         $"{built} methods in {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(built > 500, $"Only {built} GPU methods were built.");
        Assert.True(violations.IsEmpty,
            string.Join("\n", violations.OrderBy(static v => v, StringComparer.Ordinal).Take(100)));

        var actual = string.Join("\n", opaque
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Key + "\t" + pair.Value)) + "\n";
        var expectedPath = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Transpiler",
            "Ir", ExpectedFile);
        var expected = File.Exists(expectedPath) ? File.ReadAllText(expectedPath).Replace("\r", "") : "";
        if (expected != actual)
        {
            // The table of this run, to compare with the expected one and copy over it when the change is intended
            var receivedPath = RepositoryPaths.Combine("tmp", "IrOpaqueCoverage.received.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(receivedPath) ?? ".");
            File.WriteAllText(receivedPath, actual);
            output.WriteLine("Received table: " + receivedPath);
        }

        Assert.Equal(expected, actual);
    }
}
