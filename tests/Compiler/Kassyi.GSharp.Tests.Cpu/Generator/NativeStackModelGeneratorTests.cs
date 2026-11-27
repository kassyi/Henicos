#nullable enable
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;
using VerifyXunit;

namespace Kassyi.GSharp.Tests.Cpu.Generator;

[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Compiler)]
public class NativeStackModelGeneratorTests
{
    private static readonly Lazy<Compilation> s_kernelCompilation = new(CreateKernelCompilation);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public Task NativeStackModel_StackOption_MatchesVerifiedSnapshot()
    {
        var options = new Dictionary<string, string>
        {
            ["build_property.GSharpNativeBarrierModel"] = "Stack"
        };
        var (driver, _, diagnostics) = GeneratorTestRunner.RunGenerator(s_kernelCompilation.Value, "Cpu", options);
        Assert.Empty(diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        AssertLeftAligned(runResult);

        var nonPrelude = runResult.GeneratedTrees
            .Where(static t => !t.FilePath.Contains("GSharpSharedCpuPrelude", StringComparison.Ordinal))
            .OrderBy(static t => t.FilePath, StringComparer.Ordinal)
            .Select(static t => $"// File: {Path.GetFileName(t.FilePath)}\n{t.GetText()}");
        var combined = string.Join("\n\n", nonPrelude);

        // The hash of the preludes changes with every edit of a prelude, the format version with every change of the
        // record layout, so the values this compiler writes become placeholders
        return Verifier.Verify(combined, "cs")
            .UseDirectory("Snapshots")
            .UseFileName("Snap.NativeStackModel_Cpu_Stack")
            .ScrubLinesWithReplace(static line => line.Replace(
                $"GpuDeviceExportAttribute({DeviceExportRecord.CurrentFormatVersion}, " +
                $"@\"{DeviceExportRecord.CurrentPreludeHash}\"",
                "GpuDeviceExportAttribute({FormatVersion}, @\"{PreludeHash}\"",
                StringComparison.Ordinal));
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NativeStackModel_StateMachineOption_MatchesUnsetOutput()
    {
        var (unsetDriver, _, _) = GeneratorTestRunner.RunGenerator(s_kernelCompilation.Value, "Cpu", null);
        var (explicitDriver, _, _) = GeneratorTestRunner.RunGenerator(
            s_kernelCompilation.Value,
            "Cpu",
            new Dictionary<string, string> { ["build_property.GSharpNativeBarrierModel"] = "StateMachine" });

        AssertSameGeneratedTrees(unsetDriver.GetRunResult(), explicitDriver.GetRunResult());
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NativeStackModel_CudaDialectWithStackOption_MatchesDefaultCudaOutput()
    {
        var (unsetDriver, _, _) = GeneratorTestRunner.RunGenerator(s_kernelCompilation.Value, "Cuda", null);
        var (stackDriver, _, _) = GeneratorTestRunner.RunGenerator(
            s_kernelCompilation.Value,
            "Cuda",
            new Dictionary<string, string> { ["build_property.GSharpNativeBarrierModel"] = "Stack" });

        var stackResult = stackDriver.GetRunResult();
        AssertSameGeneratedTrees(unsetDriver.GetRunResult(), stackResult);

        Assert.Contains(
            stackResult.GeneratedTrees,
            static t => Path.GetFileName(t.FilePath) == "GSharpSharedCudaPrelude.g.cs");
        Assert.DoesNotContain(
            stackResult.GeneratedTrees,
            static t => Path.GetFileName(t.FilePath) == "GSharpSharedCpuPrelude.g.cs");

        foreach (var tree in stackResult.GeneratedTrees)
        {
            if (Path.GetFileName(tree.FilePath) == DeviceExportEmitter.HintName)
            {
                continue;
            }

            var text = tree.GetText().ToString();
            Assert.DoesNotContain(GSharpInternalNames.NativeStack.Barrier, text, StringComparison.Ordinal);
            Assert.DoesNotContain(GSharpInternalNames.NativeStack.Resume, text, StringComparison.Ordinal);
        }
    }

    // The CUDA text never uses the stack form, so the output alone cannot show that it was not translated; the switch
    // that the translations take can. The C++ dialect with the same option shows that the step is observed
    [Theory]
    [InlineData("Cuda", "Stack", false)]
    [InlineData("Cpu", "Stack", true)]
    [InlineData("Cpu", "StateMachine", false)]
    [Scenario(TestScenario.Boundary)]
    public void NativeStackModel_DialectAndOption_DecideWhetherTheStackFormIsTranslated(
        string dialect, string model, bool expected)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new GpuKernelGenerator().AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            optionsProvider: new TestAnalyzerConfigOptionsProvider(
                dialect,
                new Dictionary<string, string> { ["build_property.GSharpNativeBarrierModel"] = model }),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));

        var result = driver.RunGenerators(s_kernelCompilation.Value).GetRunResult().Results.Single();

        Assert.Null(result.Exception);
        var output = Assert.Single(result.TrackedSteps[NativeStackPipeline.SwitchTrackingName]
            .SelectMany(static run => run.Outputs));
        Assert.Equal(expected, Assert.IsType<bool>(output.Value));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void NativeStackModel_PreludeConstant_IncludesStackThreadsOnlyWhenStackEnabled()
    {
        var (stackDriver, _, _) = GeneratorTestRunner.RunGenerator(
            s_kernelCompilation.Value,
            "Cpu",
            new Dictionary<string, string> { ["build_property.GSharpNativeBarrierModel"] = "Stack" });
        var (defaultDriver, _, _) = GeneratorTestRunner.RunGenerator(s_kernelCompilation.Value, "Cpu", null);

        var stackPrelude = GetPreludeText(stackDriver.GetRunResult());
        var defaultPrelude = GetPreludeText(defaultDriver.GetRunResult());

        Assert.Contains(GSharpInternalNames.NativeStack.Resume, stackPrelude, StringComparison.Ordinal);
        Assert.DoesNotContain(GSharpInternalNames.NativeStack.Resume, defaultPrelude, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task RecursiveSuspendingCallee_StackModelBuild_ReportsGsh0061WithoutStackEmission()
    {
        var source = ReadCase("NativeStackRecursiveCalleeCases.cs");

        var compilation = RoslynTestUtility.CreateCompilation(source).WithAssemblyName("RecursiveStackDiagTest");
        var stackOptions = new Dictionary<string, string>
        {
            ["build_property.GSharpNativeBarrierModel"] = "Stack"
        };

        var analyzerDiagnostics = await GeneratorTestRunner.GetAnalyzerDiagnosticsAsync(
            source,
            new BarrierUsageAnalyzer(),
            "Cpu",
            stackOptions);
        Assert.Contains(analyzerDiagnostics, static d => d.Id == "GSH0061");

        var (driver, _, generatorDiagnostics) = GeneratorTestRunner.RunGenerator(compilation, "Cpu", stackOptions);
        Assert.Empty(generatorDiagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

        var nonPrelude = driver.GetRunResult().GeneratedTrees
            .Where(static t => !t.FilePath.Contains("GSharpSharedCpuPrelude", StringComparison.Ordinal))
            .Select(static t => t.GetText().ToString());
        var combined = string.Join("\n", nonPrelude);
        Assert.DoesNotContain(GSharpInternalNames.NativeStack.Resume, combined, StringComparison.Ordinal);
        Assert.DoesNotContain("#error", combined, StringComparison.Ordinal);
    }

    // An operator macro whose target reaches a warp collective: its expansion has no frame of the stack model, so the
    // kernel keeps its state machine instead of a stack form that calls the target without one
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void WarpOperatorMacroKernel_StackModelBuild_StaysOnTheStateMachine()
    {
        var source = ReadCase("NativeStackWarpOperatorMacroCases.cs");

        var compilation = RoslynTestUtility.CreateCompilation(source).WithAssemblyName("WarpMacroStackTest");
        var (driver, _, _) = GeneratorTestRunner.RunGenerator(compilation, "Cpu",
            new Dictionary<string, string> { ["build_property.GSharpNativeBarrierModel"] = "Stack" });

        var combined = string.Join("\n", driver.GetRunResult().GeneratedTrees
            .Where(static t => !t.FilePath.Contains("GSharpSharedCpuPrelude", StringComparison.Ordinal))
            .Select(static t => t.GetText().ToString()));
        Assert.DoesNotContain(GSharpInternalNames.NativeStack.Resume, combined, StringComparison.Ordinal);
        Assert.DoesNotContain(GSharpInternalNames.StackWarp.Begin, combined, StringComparison.Ordinal);
        Assert.Contains("__gsharp_context_WarpMacroStackTest__Kernels__MacroAfterBarrierKernel", combined,
            StringComparison.Ordinal);
    }

    // A warp collective, or a call that reaches one, in a struct member is the user's GSH0062 under either model: the
    // stack form does not treat the missing frame there as a bug of its own
    [Theory]
    [InlineData("StateMachine")]
    [InlineData("Stack")]
    [Scenario(TestScenario.SadPath)]
    public void WarpCollectiveInStructMember_EitherModel_ReportsGsh0062(string model)
    {
        var source = ReadCase("NativeStackWarpStructMemberCases.cs");

        var compilation = RoslynTestUtility.CreateCompilation(source).WithAssemblyName("WarpStructMemberTest");
        var (driver, _, _) = GeneratorTestRunner.RunGenerator(compilation, "Cpu",
            new Dictionary<string, string> { ["build_property.GSharpNativeBarrierModel"] = model });

        var combined = string.Join("\n", driver.GetRunResult().GeneratedTrees
            .Where(static t => !t.FilePath.Contains("GSharpSharedCpuPrelude", StringComparison.Ordinal))
            .Select(static t => t.GetText().ToString()));
        Assert.DoesNotContain("internal error", combined, StringComparison.Ordinal);
        Assert.Contains("GSH0062: 'ShuffleXorSync' is a warp shuffle, so it cannot be called in a property accessor",
            combined, StringComparison.Ordinal);
        Assert.Contains("GSH0062: 'PairSum' is a device function that reaches Gpu.SyncThreads() or a warp shuffle, " +
            "so it cannot be called in a conversion operator", combined, StringComparison.Ordinal);
        Assert.Contains("so it cannot be called in a method of a struct", combined, StringComparison.Ordinal);
    }

    // A device function that passes a warp-collective-reaching device function as a method group cannot pass
    // __gsharp_warp_frame, so it stays on the state machine without emitting a native stack overload signature
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void MethodGroupToWarpCollective_StackModelBuild_OmitsNativeStackOverload()
    {
        var source = RoslynTestUtility.ReadTestCase("Generators/ManagedStackMethodGroupFallbackCases.cs");
        var compilation = RoslynTestUtility.CreateCompilation(source).WithAssemblyName("NativeWarpGroupStackTest");
        var (driver, _, _) = GeneratorTestRunner.RunGenerator(compilation, "Cpu",
            new Dictionary<string, string> { ["build_property.GSharpNativeBarrierModel"] = "Stack" });

        var deviceCode = driver.GetRunResult().GeneratedTrees
            .Single(static t => Path.GetFileName(t.FilePath) == "GSharpSharedCpuDeviceCode.g.cs")
            .GetText().ToString();
        const string InnerStepName =
            "__gsharp_fn_ManagedStackMethodGroupFallbackCases__MethodGroupFallbackKernels__InnerStep";
        Assert.Contains($"inline bool {InnerStepName}(", deviceCode, StringComparison.Ordinal);
        Assert.DoesNotContain($"inline void {InnerStepName}(", deviceCode, StringComparison.Ordinal);
    }

    private static string GetPreludeText(GeneratorDriverRunResult result) =>
        result.GeneratedTrees
            .Single(static t => Path.GetFileName(t.FilePath) == "GSharpSharedCpuPrelude.g.cs")
            .GetText()
            .ToString();

    private static void AssertSameGeneratedTrees(GeneratorDriverRunResult expected, GeneratorDriverRunResult actual)
    {
        var expectedTrees = expected.GeneratedTrees.OrderBy(static t => t.FilePath, StringComparer.Ordinal).ToArray();
        var actualTrees = actual.GeneratedTrees.OrderBy(static t => t.FilePath, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedTrees.Length, actualTrees.Length);
        for (int i = 0; i < expectedTrees.Length; i++)
        {
            Assert.Equal(expectedTrees[i].FilePath, actualTrees[i].FilePath);
            Assert.Equal(expectedTrees[i].GetText().ToString(), actualTrees[i].GetText().ToString());
        }
    }

    private static void AssertLeftAligned(GeneratorDriverRunResult runResult)
    {
        foreach (var tree in runResult.GeneratedTrees)
        {
            foreach (var line in tree.GetText().Lines)
            {
                var text = line.ToString();
                if (text.Length > 0 && char.IsWhiteSpace(text[0]))
                {
                    Assert.Fail($"{Path.GetFileName(tree.FilePath)}({line.LineNumber + 1}) is indented: {text}");
                }
            }
        }
    }

    private static string ReadCase(string fileName) =>
        File.ReadAllText(RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Generator", "Cases", fileName));

    private static Compilation CreateKernelCompilation()
    {
        var path = RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Core", "NativeStackKernels.cu.cs");
        return RoslynTestUtility.CreateCompilationAt(path, File.ReadAllText(path))
            .WithAssemblyName("NativeStackSnapshotTest");
    }
}
