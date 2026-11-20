#nullable enable
using System.Threading.Tasks;
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
        const string Source = """
            using Kassyi.GSharp.Core;

            namespace RecursiveStackDiagTest;

            public static partial class RecursiveKernels
            {
                [GpuDeviceFunction]
                public static void DirectlyRecursiveBarrier(int depth)
                {
                    if (depth <= 0)
                    {
                        return;
                    }

                    Gpu.SyncThreads();
                    DirectlyRecursiveBarrier(depth - 1);
                }

                [GpuDeviceFunction]
                public static void NonRecursiveWrapper(int depth)
                {
                    DirectlyRecursiveBarrier(depth);
                }

                [GpuKernel]
                public static void DirectRecursionKernel(int depth)
                {
                    DirectlyRecursiveBarrier(depth);
                }

                [GpuKernel]
                public static void WrapperRecursionKernel(int depth)
                {
                    NonRecursiveWrapper(depth);
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(Source).WithAssemblyName("RecursiveStackDiagTest");
        var stackOptions = new Dictionary<string, string>
        {
            ["build_property.GSharpNativeBarrierModel"] = "Stack"
        };

        var analyzerDiagnostics = await GeneratorTestRunner.GetAnalyzerDiagnosticsAsync(
            Source,
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
        const string Source = """
            using Kassyi.GSharp.Core;

            namespace WarpMacroStackTest;

            public struct WarpSum
            {
                public int V;

                [GpuOperatorMacro(typeof(WarpSumOps), nameof(WarpSumOps.Add))]
                public static WarpSum operator +(WarpSum left, WarpSum right) => throw new GpuOnlyException();
            }

            public static partial class WarpSumOps
            {
                [GpuDeviceFunction]
                public static void Add(ref WarpSum result, WarpSum left, WarpSum right)
                {
                    result.V = left.V + Warp.ShuffleXorSync(0xFFFFFFFFu, right.V, 1);
                }
            }

            public static partial class Kernels
            {
                [GpuKernel]
                public static void MacroAfterBarrierKernel(int[] data)
                {
                    var x = new WarpSum { V = data[ThreadIndex.X] };
                    Gpu.SyncThreads();
                    WarpSum z;
                    z = x + x;
                    data[ThreadIndex.X] = z.V;
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(Source).WithAssemblyName("WarpMacroStackTest");
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
        const string Source = """
            using Kassyi.GSharp.Core;

            namespace WarpStructMemberTest;

            public struct Guard : System.IDisposable
            {
                public int V;

                public int Shuffled => Warp.ShuffleXorSync(0xFFFFFFFFu, V, 1);

                public void Dispose()
                {
                    V = Ops.PairSum(V);
                }

                public static implicit operator int(Guard g) => Ops.PairSum(g.V);
            }

            public static partial class Ops
            {
                [GpuDeviceFunction]
                public static int PairSum(int v) => v + Warp.ShuffleXorSync(0xFFFFFFFFu, v, 1);

                [GpuKernel]
                public static void PropertyKernel(int[] data)
                {
                    int t = ThreadIndex.X;
                    var g = new Guard { V = data[t] };
                    Gpu.SyncThreads();
                    data[t] = g.Shuffled;
                }

                [GpuKernel]
                public static void ConversionKernel(int[] data)
                {
                    int t = ThreadIndex.X;
                    var g = new Guard { V = data[t] };
                    Gpu.SyncThreads();
                    int v = g;
                    data[t] = v;
                }

                [GpuKernel]
                public static void UsingKernel(int[] data)
                {
                    int t = ThreadIndex.X;
                    Gpu.SyncThreads();
                    using (var g = new Guard { V = data[t] })
                    {
                        data[t] = g.V;
                    }
                }
            }
            """;

        var compilation = RoslynTestUtility.CreateCompilation(Source).WithAssemblyName("WarpStructMemberTest");
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

    private static Compilation CreateKernelCompilation()
    {
        var path = RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Cpu", "Core", "NativeStackKernels.cu.cs");
        return RoslynTestUtility.CreateCompilationAt(path, File.ReadAllText(path))
            .WithAssemblyName("NativeStackSnapshotTest");
    }
}
