using System.Text;
using Kassyi.GSharp.Tests.Shared.Helpers;

#nullable enable

namespace Kassyi.GSharp.Tests.Shared.Generators;

/// <summary>
/// Records which steps of <see cref="GpuKernelGenerator"/> rerun after an edit (<c>Unchanged</c> / <c>Modified</c>)
/// and which are skipped (<c>Cached</c>). The snapshot is a measurement of the current pipeline, not a target: a change
/// to the pipeline shows up as a reviewed snapshot diff (#383).
/// </summary>
[Category(TestCategory.Compiler)]
public class GpuKernelGeneratorCachingTests(ITestOutputHelper output)
{
    private static readonly string s_sourceCode =
        RoslynTestUtility.ReadTestCase("Generators/GpuKernel_CachedStep.cu.cs");

    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public Task Test_StepReasons_AfterEdits(string dialect)
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(dialect).RunGenerators(compilation);
        var report = new StringBuilder();
        Record(report, "First run", driver);

        var unrelated = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText("namespace Other { class Unrelated { } }", s_parseOptions));
        Record(report, "After adding an unrelated file", driver.RunGenerators(unrelated));

        // One overload's body changes; the other kernels and the device function do not
        var tree = compilation.SyntaxTrees.Single();
        var editedText = tree.GetText().ToString().Replace("data[i] = data[i] * factor;", "data[i] = data[i] * factor + 1;");
        Assert.NotEqual(tree.GetText().ToString(), editedText);
        var edited = compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(editedText, s_parseOptions));
        Record(report, "After editing one kernel body", driver.RunGenerators(edited));

        var snapshots = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Generators", "Snapshots");
        Directory.CreateDirectory(snapshots);
        return Verifier.Verify(report.ToString(), "txt")
            .UseDirectory(snapshots)
            .UseFileName($"Snap.GpuKernelGeneratorCaching_{dialect}");
    }

    // Writes the run reasons of each tracked step; the elapsed times vary by machine, so they go to the test output only
    private void Record(StringBuilder report, string title, GeneratorDriver driver)
    {
        var result = driver.GetRunResult().Results.Single();
        report.AppendLine($"## {title}");
        if (result.Exception is { } exception)
        {
            report.AppendLine($"Generator failed: {exception.GetType().Name}: {exception.Message}");
        }

        var steps = result.TrackedSteps
            .Where(static s => s.Key is GpuKernelGenerator.KernelTrackingName or GpuKernelGenerator.DeviceFunctionTrackingName)
            .Concat(result.TrackedOutputSteps)
            .OrderBy(static s => s.Key, StringComparer.Ordinal);
        foreach (var (name, runs) in steps)
        {
            var reasons = runs
                .SelectMany(static r => r.Outputs)
                .GroupBy(static o => o.Reason)
                .OrderBy(static g => g.Key)
                .Select(static g => $"{g.Key} x{g.Count()}");
            report.AppendLine($"{name}: {string.Join(", ", reasons)}");

            var elapsed = runs.Aggregate(TimeSpan.Zero, static (sum, r) => sum + r.ElapsedTime);
            output.WriteLine($"{title} / {name}: {elapsed.TotalMilliseconds:F1} ms");
        }

        report.AppendLine();
    }

    private static CSharpCompilation CreateCompilation() =>
        CSharpCompilation.Create("CachingTest")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true))
            .AddReferences(RoslynTestUtility.BaseReferences)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(s_sourceCode, s_parseOptions));

    private static GeneratorDriver CreateDriver(string dialect) =>
        CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: s_parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
}
