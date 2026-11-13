using Kassyi.GSharp.Compiler.Aot;
using System.Text;
using Kassyi.GSharp.Compiler.Marshaling;
using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis.Text;

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

    private static readonly string s_overloadA =
        RoslynTestUtility.ReadTestCase("Generators/GpuKernel_SplitOverload_A.cu.cs");

    private static readonly string s_overloadB =
        RoslynTestUtility.ReadTestCase("Generators/GpuKernel_SplitOverload_B.cu.cs");

    private static readonly string s_overloadC =
        RoslynTestUtility.ReadTestCase("Generators/GpuKernel_SplitOverload_C.cu.cs");

    private static readonly string s_twoClasses =
        RoslynTestUtility.ReadTestCase("Generators/GpuKernel_TwoClasses.cu.cs");

    private static readonly string s_deviceFunctionsOnly =
        RoslynTestUtility.ReadTestCase("Generators/GpuDevice_FunctionsOnly.cu.cs");

    private static readonly string s_dynamicSharedMemoryCaller =
        RoslynTestUtility.ReadTestCase("Generators/GpuKernel_DynamicSharedMemoryCaller.cu.cs");

    private static readonly string s_dynamicSharedMemoryCallee =
        RoslynTestUtility.ReadTestCase("Generators/GpuDevice_DynamicSharedMemoryCallee.cu.cs");

    private static readonly string s_managedVersionCaller =
        RoslynTestUtility.ReadTestCase("Generators/GpuKernel_ManagedVersionCaller.cu.cs");

    private static readonly string s_managedVersionCallee =
        RoslynTestUtility.ReadTestCase("Generators/GpuDevice_ManagedVersionCallee.cu.cs");

    private const string UnrelatedSource = "namespace Other { class Unrelated { } }";

    private static readonly CSharpParseOptions s_parseOptions = new(LanguageVersion.Preview);

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public Task StepReasons_AfterEdits(string dialect)
    {
        var unrelated = CreateCompilation(("", s_sourceCode), ("", UnrelatedSource));
        var compilation = unrelated.RemoveSyntaxTrees(unrelated.SyntaxTrees[1]);
        var driver = CreateDriver(dialect).RunGenerators(compilation);
        var report = new StringBuilder();
        Record(report, "First run", driver);

        Record(report, "After adding an unrelated file", driver.RunGenerators(unrelated));

        Record(report, "After editing one kernel body", driver.RunGenerators(EditOneKernel(compilation)));

        var snapshots = RepositoryPaths.Combine("tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Generators", "Snapshots");
        Directory.CreateDirectory(snapshots);
        return Verifier.Verify(report.ToString(), "txt")
            .UseDirectory(snapshots)
            .UseFileName($"Snap.GpuKernelGeneratorCaching_{dialect}");
    }

    // #472: the unchanged kernels come from the cache with the previous compilation's class symbol, which used to split
    // the class into two groups and add its wrapper file twice
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void EditingOneKernelOfAClass_EmitsTheClassOnce(string dialect)
    {
        var compilation = CreateCompilation();
        var driver = CreateDriver(dialect).RunGenerators(compilation).RunGenerators(EditOneKernel(compilation));

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var wrappers = result.GeneratedSources
            .Where(static s => s.HintName.EndsWith("CachingKernels.g.cs", StringComparison.Ordinal))
            .ToList();
        Assert.Single(wrappers);
        // The embedded kernel source is the edited one (int arithmetic is a Prelude call, #722)
        Assert.Contains("factor),1)", wrappers[0].SourceText.ToString(), StringComparison.Ordinal);
    }

    // An edit to one class's kernel reruns that class's wrapper only: each class model compares on its own
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void EditingOneClass_LeavesTheOtherWrapperCached(string dialect)
    {
        var compilation = CreateCompilation(s_twoClasses);
        var tree = compilation.SyntaxTrees.Single();
        var text = tree.GetText().ToString();
        var editedText = text.Replace("data[i] * 2f", "data[i] * 3f", StringComparison.Ordinal);
        Assert.NotEqual(text, editedText);
        var edited = WithChangedText(compilation, tree, editedText);

        var driver = CreateDriver(dialect).RunGenerators(compilation).RunGenerators(edited);

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var reasons = result.TrackedOutputSteps["SourceOutput"]
            .Where(static run => run.Inputs[0].Source.Name == GpuKernelGenerator.KernelWrapperTrackingName)
            .ToDictionary(
                static run => GetInputValue<(KernelClassModel Model, HostEmitOptions Options)>(run).Model.Class.Name,
                static run => Assert.Single(run.Outputs).Reason);
        Assert.Equal(IncrementalStepRunReason.Modified, reasons["FirstKernels"]);
        Assert.Equal(IncrementalStepRunReason.Cached, reasons["SecondKernels"]);
    }

    // A project without kernels gets no shared sources, no state machines and no artifact, only the records that let
    // another assembly's kernels call its device functions (cross-assembly-device-code.md §3.3)
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void DeviceFunctionsOnly_EmitOnlyTheDeviceExports(string dialect)
    {
        var driver = CreateDriver(dialect).RunGenerators(CreateCompilation(s_deviceFunctionsOnly));

        var result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.Empty(result.Diagnostics);
        // The records, and the managed state machines that a consumer's managed fallback calls (C3)
        Assert.Equal([DeviceExportEmitter.HintName, "TestNamespace__DeviceOnly__DeviceOnlyFunctions.DeviceStateMachines.g.cs"],
            result.GeneratedSources.Select(static s => s.HintName).Order(StringComparer.Ordinal));
        // The device functions were transpiled; only the outputs are gated
        Assert.Equal(2, result.TrackedSteps[GpuKernelGenerator.DeviceFunctionTrackingName]
            .SelectMany(static run => run.Outputs)
            .Count());
    }

    // #383: the overload check derives from the kernels of one compilation, never from a cached kernel's symbol.
    // The reference output is the final compilation generated by a fresh driver.
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void AddingAnOverloadInAnotherFile_MatchesAFreshRun(string dialect)
    {
        var withBoth = CreateCompilation(("", s_overloadA), ("", s_overloadB));
        var first = withBoth.RemoveSyntaxTrees(withBoth.SyntaxTrees[1]);

        var incremental = CreateDriver(dialect).RunGenerators(first).RunGenerators(withBoth);

        AssertSameOutput(CreateDriver(dialect).RunGenerators(withBoth), incremental);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void RemovingAnOverloadInAnotherFile_MatchesAFreshRun(string dialect)
    {
        var withBoth = CreateCompilation(("", s_overloadA), ("", s_overloadB));
        var onlyA = withBoth.RemoveSyntaxTrees(withBoth.SyntaxTrees[1]);

        var incremental = CreateDriver(dialect).RunGenerators(withBoth).RunGenerators(onlyA);

        AssertSameOutput(CreateDriver(dialect).RunGenerators(onlyA), incremental);
    }

    // #383: GetOverloadOrdinal counts every same-named method whatever its attributes, while the overload check counts only
    // the [GpuKernel] ones. Giving a sibling the attribute therefore changes the overload check but not the transpiled
    // result of the float[] kernel, which the pipeline then takes from the cache; the class model must still see the sibling.
    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void MarkingASiblingAsKernel_MatchesAFreshRun(string dialect)
    {
        var plain = s_overloadC;
        var marked = MarkAsKernel(plain);
        var before = CreateSiblingCompilation(plain);
        var after = CreateSiblingCompilation(marked);

        var incremental = CreateDriver(dialect).RunGenerators(before).RunGenerators(after);

        AssertFloatKernelUnchanged(incremental);
        AssertSameOutput(CreateDriver(dialect).RunGenerators(after), incremental);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void UnmarkingASiblingKernel_MatchesAFreshRun(string dialect)
    {
        var plain = s_overloadC;
        var marked = MarkAsKernel(plain);
        var before = CreateSiblingCompilation(marked);
        var after = CreateSiblingCompilation(plain);

        var incremental = CreateDriver(dialect).RunGenerators(before).RunGenerators(after);

        AssertFloatKernelUnchanged(incremental);
        AssertSameOutput(CreateDriver(dialect).RunGenerators(after), incremental);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public void EditingIteratorInAnotherFile_ReemitsConsumerKernel(string dialect)
    {
        const string KernelSource = """
            using Kassyi.GSharp.Core;

            namespace TestNamespace.IteratorCaching;

            public static partial class IteratorCachingKernels
            {
                [GpuKernel]
                public static void ConsumeIteratorKernel(int[] output)
                {
                    int acc = 0;
                    foreach (var x in IteratorHelpers.Values(3))
                    {
                        acc += x;
                    }

                    output[0] = acc;
                }
            }
            """;

        const string IteratorBefore = """
            using System.Collections.Generic;
            using Kassyi.GSharp.Core;

            namespace TestNamespace.IteratorCaching;

            public static class IteratorHelpers
            {
                [GpuDeviceFunction]
                public static IEnumerable<int> Values(int seed)
                {
                    yield return seed + 10;
                }
            }
            """;

        var iteratorAfter = IteratorBefore.Replace("seed + 10", "seed + 20", StringComparison.Ordinal);
        Assert.NotEqual(IteratorBefore, iteratorAfter);

        var before = CreateCompilation(("kernel.cs", KernelSource), ("iterator.cs", IteratorBefore));
        var after = WithChangedText(before, before.SyntaxTrees[1], iteratorAfter);

        var incremental = CreateDriver(dialect).RunGenerators(before).RunGenerators(after);

        var result = incremental.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var kernelOutputs = result.TrackedSteps[GpuKernelGenerator.KernelTrackingName]
            .SelectMany(static run => run.Outputs)
            .ToList();
        var kernelOutput = Assert.Single(kernelOutputs);
        Assert.Equal(IncrementalStepRunReason.Modified, kernelOutput.Reason);

        var wrapper = Assert.Single(
            result.GeneratedSources,
            static s => s.HintName.EndsWith("IteratorCachingKernels.g.cs", StringComparison.Ordinal));
        Assert.Contains(",20)", wrapper.SourceText.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(",10)", wrapper.SourceText.ToString(), StringComparison.Ordinal);

        AssertSameOutput(CreateDriver(dialect).RunGenerators(after), incremental);
    }

    // #439: on CUDA a kernel owns the table of the dynamic shared memory regions its callees reach, so an edit that makes a
    // callee in another file declare dynamic shared memory must re-emit the caller
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CalleeInAnotherFileGainsDynamicSharedMemory_ReemitsCallerKernel()
    {
        var calleeAfter = s_dynamicSharedMemoryCallee.Replace(
            "new SharedMemory<int>(32)", "new SharedMemory<int>(n)", StringComparison.Ordinal);
        Assert.NotEqual(s_dynamicSharedMemoryCallee, calleeAfter);

        var before = CreateCompilation(
            ("kernel.cs", s_dynamicSharedMemoryCaller),
            ("callee.cs", s_dynamicSharedMemoryCallee));
        var after = WithChangedText(before, before.SyntaxTrees[1], calleeAfter);

        var first = CreateDriver("Cuda").RunGenerators(before);
        var wrapperBefore = Assert.Single(
            first.GetRunResult().Results.Single().GeneratedSources,
            static s => s.HintName.EndsWith("DynamicSharedMemoryCachingKernels.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain("__gsharp_dsmem", wrapperBefore.SourceText.ToString(), StringComparison.Ordinal);

        var incremental = first.RunGenerators(after);

        var result = incremental.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var kernelOutput = Assert.Single(result.TrackedSteps[GpuKernelGenerator.KernelTrackingName]
            .SelectMany(static run => run.Outputs));
        Assert.Equal(IncrementalStepRunReason.Modified, kernelOutput.Reason);

        var wrapper = Assert.Single(
            result.GeneratedSources,
            static s => s.HintName.EndsWith("DynamicSharedMemoryCachingKernels.g.cs", StringComparison.Ordinal));
        Assert.Contains("__gsharp_dsmem_storage;", wrapper.SourceText.ToString(), StringComparison.Ordinal);
        Assert.Contains("__gsharp_id_2_tid,__gsharp_dsmem)", wrapper.SourceText.ToString(), StringComparison.Ordinal);

        AssertSameOutput(CreateDriver("Cuda").RunGenerators(after), incremental);
    }

    // #711: a callee in another file that starts to expand an operator macro gets a managed version, and the caller's
    // managed state machine must call it
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CalleeInAnotherFileGainsOperatorMacro_ReemitsCallerAndManagedVersion()
    {
        var calleeAfter = s_managedVersionCallee.Replace("z.V = x.V + y.V;", "z = x + y;", StringComparison.Ordinal);
        Assert.NotEqual(s_managedVersionCallee, calleeAfter);

        var before = CreateCompilation(("kernel.cs", s_managedVersionCaller), ("callee.cs", s_managedVersionCallee));
        var after = WithChangedText(before, before.SyntaxTrees[1], calleeAfter);

        var first = CreateDriver("Cpu").RunGenerators(before);
        var sourcesBefore = first.GetRunResult().Results.Single().GeneratedSources;
        Assert.DoesNotContain("__gsharp_managed_", GetWrapper(sourcesBefore), StringComparison.Ordinal);
        Assert.DoesNotContain(sourcesBefore,
            static s => s.HintName.EndsWith("ManagedVersionHelpers.DeviceStateMachines.g.cs", StringComparison.Ordinal));

        var incremental = first.RunGenerators(after);

        var result = incremental.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        var kernelOutput = Assert.Single(result.TrackedSteps[GpuKernelGenerator.KernelTrackingName]
            .SelectMany(static run => run.Outputs));
        Assert.Equal(IncrementalStepRunReason.Modified, kernelOutput.Reason);

        var managedName = GSharpInternalNames.ManagedVersion.MethodPrefix +
                          "TestNamespace__ManagedVersionCaching__ManagedVersionHelpers__Sum";
        Assert.Contains($"ManagedVersionHelpers.{managedName}(tid, n)", GetWrapper(result.GeneratedSources),
            StringComparison.Ordinal);
        var version = Assert.Single(
            result.GeneratedSources,
            static s => s.HintName.EndsWith("ManagedVersionHelpers.DeviceStateMachines.g.cs", StringComparison.Ordinal));
        Assert.Contains($"public static int {managedName}(int a, int b)", version.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.Contains("ManagedVersionHelpers.Add(ref z, x, y);", version.SourceText.ToString(),
            StringComparison.Ordinal);

        AssertSameOutput(CreateDriver("Cpu").RunGenerators(after), incremental);
    }

    // An edit that changes no classification leaves the kernel and the managed versions as they were
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void UnrelatedEdit_LeavesManagedVersionsCached()
    {
        var callee = s_managedVersionCallee.Replace("z.V = x.V + y.V;", "z = x + y;", StringComparison.Ordinal);
        var unrelated = CreateCompilation(
            ("kernel.cs", s_managedVersionCaller),
            ("callee.cs", callee),
            ("other.cs", UnrelatedSource));
        var compilation = unrelated.RemoveSyntaxTrees(unrelated.SyntaxTrees[2]);

        var incremental = CreateDriver("Cpu").RunGenerators(compilation).RunGenerators(unrelated);

        var result = incremental.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.All(
            result.TrackedSteps[GpuKernelGenerator.KernelTrackingName].SelectMany(static run => run.Outputs),
            static o => Assert.NotEqual(IncrementalStepRunReason.Modified, o.Reason));
        Assert.All(
            result.TrackedSteps[GpuKernelGenerator.DeviceFunctionTrackingName].SelectMany(static run => run.Outputs),
            static o => Assert.NotEqual(IncrementalStepRunReason.Modified, o.Reason));
        var stateMachineOutputs = result.TrackedOutputSteps["SourceOutput"]
            .Where(static run => run.Inputs[0].Source.Name == GpuKernelGenerator.DeviceStateMachinesTrackingName)
            .SelectMany(static run => run.Outputs)
            .ToList();
        Assert.NotEmpty(stateMachineOutputs);
        Assert.All(stateMachineOutputs, static o => Assert.Equal(IncrementalStepRunReason.Cached, o.Reason));
    }

    private static string GetWrapper(ImmutableArray<GeneratedSourceResult> sources) =>
        Assert.Single(
            sources,
            static s => s.HintName.EndsWith("ManagedVersionCachingKernels.g.cs", StringComparison.Ordinal)
        ).SourceText.ToString();

    // The paths fix the declaration order of the partial class's parts
    private static CSharpCompilation CreateSiblingCompilation(string siblingSource) =>
        CreateCompilation(("a.cs", s_overloadA), ("c.cs", siblingSource));

    private static string MarkAsKernel(string siblingSource)
    {
        const string Signature = "public static void ScaleKernel(double[] data, double factor)";
        var marked = siblingSource.Replace(
            Signature,
            "[GpuKernel]\n    [SkipSnapshotConsistency(\"Generator case\")]\n    " + Signature,
            StringComparison.Ordinal);
        Assert.NotEqual(siblingSource, marked);
        return marked;
    }

    // Without this the test would not reach the path it exists for: a kernel from the cache next to a changed sibling
    private static void AssertFloatKernelUnchanged(GeneratorDriver incremental)
    {
        var outputs = incremental.GetRunResult().Results.Single().TrackedSteps[GpuKernelGenerator.KernelTrackingName]
            .SelectMany(static run => run.Outputs)
            .Where(static o => o.Value is TranspiledKernelResult kernel &&
                kernel.Host.Parameters[0].TypeName == "global::System.Single[]")
            .ToList();
        var output = Assert.Single(outputs);
        Assert.Equal(IncrementalStepRunReason.Unchanged, output.Reason);
    }

    private static void AssertSameOutput(GeneratorDriver fresh, GeneratorDriver incremental)
    {
        var expected = fresh.GetRunResult().Results.Single();
        var actual = incremental.GetRunResult().Results.Single();
        Assert.Null(expected.Exception);
        Assert.Null(actual.Exception);

        var expectedSources = expected.GeneratedSources.OrderBy(static s => s.HintName, StringComparer.Ordinal).ToList();
        var actualSources = actual.GeneratedSources.OrderBy(static s => s.HintName, StringComparer.Ordinal).ToList();

        var expectedNames = string.Join(", ", expectedSources.Select(static s => s.HintName));
        var actualNames = string.Join(", ", actualSources.Select(static s => s.HintName));
        Assert.True(
            expectedNames == actualNames,
            $"Hint names differ. Fresh: [{expectedNames}]; incremental: [{actualNames}]");

        for (int i = 0; i < expectedSources.Count; i++)
        {
            var expectedText = expectedSources[i].SourceText.ToString();
            var actualText = actualSources[i].SourceText.ToString();
            if (expectedText != actualText)
            {
                var hintName = expectedSources[i].HintName;
                Assert.Fail($"'{hintName}' differs at {FirstDifferentLine(expectedText, actualText)}");
            }
        }
    }

    // line <n>, with the fresh and the incremental text of the first line that differs
    private static string FirstDifferentLine(string fresh, string incremental)
    {
        var freshLines = fresh.AsSpan().EnumerateLines();
        var incrementalLines = incremental.AsSpan().EnumerateLines();
        for (var line = 1; ; line++)
        {
            var hasFresh = freshLines.MoveNext();
            var hasIncremental = incrementalLines.MoveNext();
            var freshLine = hasFresh ? freshLines.Current.ToString() : "<end of text>";
            var incrementalLine = hasIncremental ? incrementalLines.Current.ToString() : "<end of text>";
            if (freshLine != incrementalLine || !hasFresh || !hasIncremental)
            {
                return $"line {line}.{Environment.NewLine}" +
                       $"Fresh:       {freshLine}{Environment.NewLine}Incremental: {incrementalLine}";
            }
        }
    }

    // One overload's body changes; the other kernels and the device function do not
    private static Compilation EditOneKernel(Compilation compilation)
    {
        var tree = compilation.SyntaxTrees.Single();
        var text = tree.GetText().ToString();
        var editedText = text.Replace(
            "data[i] = data[i] * factor;",
            "data[i] = data[i] * factor + 1;",
            StringComparison.Ordinal);
        Assert.NotEqual(text, editedText);
        return WithChangedText(compilation, tree, editedText);
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

        // Each output is named after the tracking name of its input, so the report shows which outputs reran
        var outputs = result.TrackedOutputSteps["SourceOutput"]
            .GroupBy(static run => run.Inputs[0].Source.Name ?? "SourceOutput")
            .Select(static g => new KeyValuePair<string, ImmutableArray<IncrementalGeneratorRunStep>>(
                "SourceOutput(" + g.Key + ")", [.. g]));
        var steps = result.TrackedSteps
            .Where(static s => s.Key is GpuKernelGenerator.KernelTrackingName or GpuKernelGenerator.DeviceFunctionTrackingName
                or GpuKernelGenerator.KernelClassTrackingName)
            .Concat(outputs)
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

    private static T GetInputValue<T>(IncrementalGeneratorRunStep run)
    {
        var (source, index) = run.Inputs[0];
        return Assert.IsType<T>(source.Outputs[index].Value);
    }

    private static CSharpCompilation CreateCompilation() => CreateCompilation(s_sourceCode);

    private static CSharpCompilation CreateCompilation(string source) => CreateCompilation(("", source));

    private static CSharpCompilation CreateCompilation(params (string Path, string Source)[] files) =>
        RoslynTestUtility.CreateCompilationAt(files).WithAssemblyName("CachingTest");

    // The compilation after an edit of one file, as an editor changes it: the tree keeps its path and parse options
    private static Compilation WithChangedText(Compilation compilation, SyntaxTree tree, string text) =>
        compilation.ReplaceSyntaxTree(tree, tree.WithChangedText(SourceText.From(text)));

    private static GeneratorDriver CreateDriver(string dialect) =>
        CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: s_parseOptions,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
}
