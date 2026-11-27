#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

[Category(TestCategory.Compiler)]
public class StackModelLiftDiagnosticsTests
{
    private const string LiftTestCase = "Analyzers/BarrierUsage/StackModelLiftKernels.cu.cs";

    private static string Gsh0003 => GSharpDiagnostics.UnsupportedSyntax.Id;

    private static string Gsh0039 => GSharpDiagnostics.CrossBarrierRefStructProhibited.Id;

    private static string Gsh0041 => GSharpDiagnostics.CrossBarrierLocalPointerProhibited.Id;

    private static string Gsh0121 => GSharpDiagnostics.GotoToChainedLabelAcrossStates.Id;

    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Cpu", null, null)]
    [InlineData("Cpu", "Stack", null)]
    [InlineData("Cpu", null, "Stack")]
    [InlineData("Cuda", null, null)]
    public async Task CrossBarrierLocals_NotBothOnStacks_ReportsAllFormsWithDefaultMessages(
        string dialect,
        string? nativeModel,
        string? managedModel)
    {
        var run = await RunLiftCasesAsync(dialect, nativeModel, managedModel);

        Assert.Equal(
            [
                (13, Gsh0039),
                (22, Gsh0039),
                (30, Gsh0039),
                (37, Gsh0039),
                (47, Gsh0039),
                (58, Gsh0041),
                (66, Gsh0041),
                (82, Gsh0039),
                (90, Gsh0041)
            ],
            OrderByLine(run.Analyzer, Gsh0039, Gsh0041));

        var messages = run.Analyzer
            .Where(d => d.Id == Gsh0039 || d.Id == Gsh0041)
            .OrderBy(static d => LineOf(d))
            .ToArray();

        Assert.Equal(
            "A ref or ref struct variable 'localSpan' cannot be held across a Gpu.SyncThreads() barrier. " +
            "This is restricted by the C# state machine model.",
            messages[0].GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(
            "A ref or ref struct variable 'chain' cannot be held across a Gpu.SyncThreads() barrier. " +
            "This is restricted by the C# state machine model.",
            messages[1].GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(
            "A ref or ref struct variable 'first' cannot be held across a Gpu.SyncThreads() barrier. " +
            "This is restricted by the C# state machine model.",
            messages[2].GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(
            "A ref or ref struct variable 's' cannot be held across a Gpu.SyncThreads() barrier. " +
            "This is restricted by the C# state machine model.",
            messages[3].GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(
            "A ref or ref struct variable 'element' cannot be held across a Gpu.SyncThreads() barrier. " +
            "This is restricted by the C# state machine model.",
            messages[4].GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(
            "A pointer to a local variable or stackalloc block cannot cross a Gpu.SyncThreads() barrier " +
            "because the CPU emulator rewinds the stack frame.",
            messages[5].GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(
            "A pointer to a local variable or stackalloc block cannot cross a Gpu.SyncThreads() barrier " +
            "because the CPU emulator rewinds the stack frame.",
            messages[6].GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(
            "A ref or ref struct variable 'held' cannot be held across a Gpu.SyncThreads() barrier. " +
            "This is restricted by the C# state machine model.",
            messages[7].GetMessage(CultureInfo.InvariantCulture));
        Assert.Equal(
            "A pointer to a local variable or stackalloc block cannot cross a Gpu.SyncThreads() barrier " +
            "because the CPU emulator rewinds the stack frame.",
            messages[8].GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("Stack", "Stack")]
    [InlineData("stack", "STACK")]
    public async Task CrossBarrierLocals_CpuBothOnStacks_LiftsEligibleAndExplainsFallback(
        string nativeModel,
        string managedModel)
    {
        var run = await RunLiftCasesAsync("Cpu", nativeModel, managedModel);

        Assert.Equal(
            [
                (82, Gsh0039),
                (90, Gsh0041)
            ],
            OrderByLine(run.Analyzer, Gsh0039, Gsh0041));

        Assert.Empty(OrderByLine(run.Generator, Gsh0039, Gsh0041));

        var messages = run.Analyzer
            .Where(d => d.Id == Gsh0039 || d.Id == Gsh0041)
            .OrderBy(static d => LineOf(d))
            .Select(static d => d.GetMessage(CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Contains("recursive", messages[0], StringComparison.Ordinal);
        Assert.Contains("recursive", messages[1], StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task CrossBarrierLocals_CudaBothOnStacks_LiftsAllKernelAndDeviceForms()
    {
        var run = await RunLiftCasesAsync("Cuda", "Stack", "Stack");

        Assert.Empty(OrderByLine(run.Analyzer, Gsh0039, Gsh0041));
        Assert.Empty(OrderByLine(run.Generator, Gsh0039, Gsh0041));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ChangedSpanParameterDeviceFunction_BothOnStacksWithExport_ReportsNoGsh0039()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace StackModelExportedDeviceCases;

            public static partial class ExportedDeviceFunctions
            {
                [GpuDeviceFunction]
                public static int ChangedSpanParameter(Span<int> s)
                {
                    s = s.Slice(1);
                    Gpu.SyncThreads();
                    return s[0];
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var options = BuildOptions("Stack", "Stack", exportDeviceCode: "true");

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu", options);

        Assert.Empty(OrderByLine(run.Analyzer, Gsh0039, Gsh0041));
        Assert.Empty(OrderByLine(run.Generator, Gsh0039, Gsh0041));
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task SafetyNet_KernelFallsBackOnlyOnManagedPath_GeneratorReportsGsh0039WithManagedReason()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace StackModelSafetyNetCases;

            public struct NonPartialMacroSum
            {
                public int V;

                [GpuOperatorMacro(typeof(NonPartialMacroOps), nameof(NonPartialMacroOps.Add))]
                public static NonPartialMacroSum operator +(NonPartialMacroSum left, NonPartialMacroSum right) =>
                    throw new GpuOnlyException();
            }

            public static class NonPartialMacroOps
            {
                [GpuDeviceFunction]
                public static void Add(ref NonPartialMacroSum result, NonPartialMacroSum left, NonPartialMacroSum right)
                {
                    Gpu.SyncThreads();
                    result.V = left.V + right.V;
                }
            }

            public static partial class SafetyNetKernels
            {
                [GpuDeviceFunction]
                public static int LiftedCalleeWithHeldSpan(Span<int> input)
                {
                    Span<int> sliced = input.Slice(1);
                    Gpu.SyncThreads();
                    return sliced[0];
                }

                [GpuKernel]
                public static void ManagedFallbackWithHeldSpanKernel(int[] data, int[] output)
                {
                    var x = new NonPartialMacroSum { V = data[ThreadIndex.X] };
                    Span<int> held = data.AsSpan();
                    int fromCallee = LiftedCalleeWithHeldSpan(held);
                    NonPartialMacroSum z;
                    z = x + x;
                    output[ThreadIndex.X] = z.V + held[0] + fromCallee;
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var options = BuildOptions("Stack", "Stack");

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu", options);

        Assert.Empty(OrderByLine(run.Analyzer, Gsh0039, Gsh0041));

        var generatorDiagnostics = run.Generator
            .Where(d => d.Id == Gsh0039 || d.Id == Gsh0041)
            .OrderBy(static d => LineOf(d))
            .ToArray();

        Assert.Equal(2, generatorDiagnostics.Length);
        Assert.Equal(30, LineOf(generatorDiagnostics[0]));
        Assert.Equal(39, LineOf(generatorDiagnostics[1]));

        foreach (var diagnostic in generatorDiagnostics)
        {
            var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            Assert.Contains("managed", message, StringComparison.Ordinal);
            Assert.Contains("SafetyNetKernels.ManagedFallbackWithHeldSpanKernel", message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task SafetyNet_FallbackKernelWithOutSpanFromSuspendingCall_GeneratorReportsGsh0039WithManagedReason()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace StackModelSafetyNetOutSpanCases;

            public struct NonPartialMacroSum
            {
                public int V;

                [GpuOperatorMacro(typeof(NonPartialMacroOps), nameof(NonPartialMacroOps.Add))]
                public static NonPartialMacroSum operator +(NonPartialMacroSum left, NonPartialMacroSum right) =>
                    throw new GpuOnlyException();
            }

            public static class NonPartialMacroOps
            {
                [GpuDeviceFunction]
                public static void Add(ref NonPartialMacroSum result, NonPartialMacroSum left, NonPartialMacroSum right)
                {
                    Gpu.SyncThreads();
                    result.V = left.V + right.V;
                }
            }

            public static partial class SafetyNetOutSpanKernels
            {
                [GpuDeviceFunction]
                public static void StepSpan(Span<int> src, out Span<int> s)
                {
                    s = src;
                    Gpu.SyncThreads();
                }

                [GpuKernel]
                public static void ManagedFallbackWithOutSpanKernel(int[] data, int[] output)
                {
                    int tid = ThreadIndex.X;
                    var x = new NonPartialMacroSum { V = data[tid] };
                    StepSpan(data, out Span<int> s);
                    NonPartialMacroSum z;
                    z = x + x;
                    output[tid] = z.V + s[tid];
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var options = BuildOptions("Stack", "Stack");

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu", options);

        Assert.Empty(OrderByLine(run.Analyzer, Gsh0039, Gsh0041));

        var diagnostic = Assert.Single(run.Generator.Where(d => d.Id == Gsh0039 || d.Id == Gsh0041));
        Assert.Equal(Gsh0039, diagnostic.Id);
        Assert.Equal(39, LineOf(diagnostic));
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("managed", message, StringComparison.Ordinal);
        Assert.Contains("SafetyNetOutSpanKernels.ManagedFallbackWithOutSpanKernel", message, StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task SafetyNet_FallbackKernelWithHeldRefLocal_GeneratorReportsGsh0003()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace StackModelSafetyNetHeldRefCases;

            public struct NonPartialMacroSum
            {
                public int V;

                [GpuOperatorMacro(typeof(NonPartialMacroOps), nameof(NonPartialMacroOps.Add))]
                public static NonPartialMacroSum operator +(NonPartialMacroSum left, NonPartialMacroSum right) =>
                    throw new GpuOnlyException();
            }

            public static class NonPartialMacroOps
            {
                [GpuDeviceFunction]
                public static void Add(ref NonPartialMacroSum result, NonPartialMacroSum left, NonPartialMacroSum right)
                {
                    Gpu.SyncThreads();
                    result.V = left.V + right.V;
                }
            }

            public static partial class SafetyNetHeldRefKernels
            {
                [GpuKernel]
                public static void ManagedFallbackWithHeldRefKernel(int[] data, int[] output)
                {
                    int tid = ThreadIndex.X;
                    int x = data[tid];
                    int y = data[tid] + 1;
                    ref int alias = ref x;
                    alias = ref y;
                    alias += 1;
                    Gpu.SyncThreads();
                    NonPartialMacroSum m = new NonPartialMacroSum { V = y };
                    NonPartialMacroSum z;
                    z = m + m;
                    output[tid] = z.V;
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var options = BuildOptions("Stack", "Stack");

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu", options);

        Assert.Empty(OrderByLine(run.Analyzer, Gsh0039, Gsh0041));
        Assert.DoesNotContain(run.Analyzer, static d => d.Id == Gsh0003 && LineOf(d) == 33);

        var diagnostic = Assert.Single(run.Generator.Where(d => d.Id == Gsh0003));
        Assert.Equal(33, LineOf(diagnostic));
        Assert.Contains(
            GpuCodeConstraints.RefLocalHeldByCpuStateMachine,
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task SafetyNet_FallbackKernelsCallingLiftedCalleesWithLoweringLocals_GeneratorReportsOnce()
    {
        const string Source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace StackModelSafetyNetLiftedCalleeCases;

            public struct NonPartialMacroSum
            {
                public int V;

                [GpuOperatorMacro(typeof(NonPartialMacroOps), nameof(NonPartialMacroOps.Add))]
                public static NonPartialMacroSum operator +(NonPartialMacroSum left, NonPartialMacroSum right) =>
                    throw new GpuOnlyException();
            }

            public static class NonPartialMacroOps
            {
                [GpuDeviceFunction]
                public static void Add(ref NonPartialMacroSum result, NonPartialMacroSum left, NonPartialMacroSum right)
                {
                    Gpu.SyncThreads();
                    result.V = left.V + right.V;
                }
            }

            public static partial class SafetyNetLiftedCalleeKernels
            {
                [GpuDeviceFunction]
                public static void StepSpan(Span<int> src, out Span<int> s)
                {
                    s = src;
                    Gpu.SyncThreads();
                }

                [GpuDeviceFunction]
                public static int LiftedCalleeWithOutSpan(int[] data)
                {
                    StepSpan(data, out Span<int> s);
                    return s[ThreadIndex.X];
                }

                [GpuDeviceFunction]
                public static partial int LiftedCalleeWithHeldRef(int[] data);

                public static partial int LiftedCalleeWithHeldRef(int[] data)
                {
                    int a = data[ThreadIndex.X];
                    int b = data[ThreadIndex.X] + 1;
                    ref int alias = ref a;
                    alias = ref b;
                    alias += 1;
                    Gpu.SyncThreads();
                    return b;
                }

                [GpuKernel]
                public static void FirstFallbackCallingLiftedCalleesKernel(int[] data, int[] output)
                {
                    var x = new NonPartialMacroSum { V = data[ThreadIndex.X] };
                    int fromSpan = LiftedCalleeWithOutSpan(data);
                    int fromRef = LiftedCalleeWithHeldRef(data);
                    NonPartialMacroSum z;
                    z = x + x;
                    output[ThreadIndex.X] = z.V + fromSpan + fromRef;
                }

                [GpuKernel]
                public static void SecondFallbackCallingLiftedCalleesKernel(int[] data, int[] output)
                {
                    var x = new NonPartialMacroSum { V = data[ThreadIndex.X] };
                    int fromSpan = LiftedCalleeWithOutSpan(data);
                    int fromRef = LiftedCalleeWithHeldRef(data);
                    NonPartialMacroSum z;
                    z = x + x;
                    output[ThreadIndex.X] = z.V + fromSpan + fromRef;
                }
            }
            """;
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        var options = BuildOptions("Stack", "Stack");

        var run = await KernelDiagnostics.RunCompilationAsync(compilation, "Cpu", options);

        Assert.Empty(OrderByLine(run.Analyzer, Gsh0039, Gsh0041));
        Assert.DoesNotContain(run.Analyzer, static d => d.Id == Gsh0003 && LineOf(d) == 48);

        var spanDiagnostic = Assert.Single(run.Generator.Where(d => d.Id == Gsh0039));
        Assert.Equal(37, LineOf(spanDiagnostic));
        Assert.Contains("managed", spanDiagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);

        var refDiagnostic = Assert.Single(run.Generator.Where(d => d.Id == Gsh0003));
        Assert.Equal(48, LineOf(refDiagnostic));
        Assert.Contains(
            GpuCodeConstraints.RefLocalHeldByCpuStateMachine,
            refDiagnostic.GetMessage(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task GeneratorStateMachineDiagnostics_BothOnStacks_SuppressesHeldRefAndSpansAcrossStatesOnly()
    {
        var heldCompilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Diagnostics/RefLocalHeldByCpuStateMachine.cu.cs"));
        var heldDefault = await KernelDiagnostics.RunCompilationAsync(heldCompilation, "Cpu");
        var heldBoth = await KernelDiagnostics.RunCompilationAsync(
            heldCompilation, "Cpu", BuildOptions("Stack", "Stack"));

        Assert.NotEmpty(heldDefault.Generator.Where(d =>
            d.Id == Gsh0003 &&
            d.GetMessage(CultureInfo.InvariantCulture).Contains(
                GpuCodeConstraints.RefLocalHeldByCpuStateMachine, StringComparison.Ordinal)));
        Assert.Empty(heldBoth.Generator.Where(d =>
            d.Id == Gsh0003 &&
            d.GetMessage(CultureInfo.InvariantCulture).Contains(
                GpuCodeConstraints.RefLocalHeldByCpuStateMachine, StringComparison.Ordinal)));

        var spanCompilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Diagnostics/SpanAcrossStates.cu.cs"));
        var spanDefault = await KernelDiagnostics.RunCompilationAsync(spanCompilation, "Cpu");
        var spanBoth = await KernelDiagnostics.RunCompilationAsync(
            spanCompilation, "Cpu", BuildOptions("Stack", "Stack"));

        Assert.Equal(4, spanDefault.Generator.Count(d => d.Id == Gsh0039));
        Assert.Empty(spanBoth.Generator.Where(d => d.Id == Gsh0039));

        var gotoCompilation = RoslynTestUtility.CreateCompilation(
            RoslynTestUtility.ReadTestCase("Analyzers/BarrierUsage/ChainedLabelGotoKernels.cu.cs"));
        var gotoDefault = await KernelDiagnostics.RunCompilationAsync(gotoCompilation, "Cpu");
        var gotoBoth = await KernelDiagnostics.RunCompilationAsync(
            gotoCompilation, "Cpu", BuildOptions("Stack", "Stack"));

        Assert.Equal(
            OrderByLine(gotoDefault.Generator, Gsh0121),
            OrderByLine(gotoBoth.Generator, Gsh0121));
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task ExistingSuppressedSites_BothOnStacks_ReportsNeitherGsh0039NorGsh0041()
    {
        var splitPath = RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Differential",
            "SplitOperandOrderDifferentialKernels.cu.cs");
        var splitCompilation = RoslynTestUtility.CreateCompilationAt(splitPath, File.ReadAllText(splitPath));
        var splitDefault = await KernelDiagnostics.RunCompilationAsync(splitCompilation, "Cpu");
        var splitBoth = await KernelDiagnostics.RunCompilationAsync(
            splitCompilation, "Cpu", BuildOptions("Stack", "Stack"));

        Assert.Contains(splitDefault.Analyzer, static d => d.Id == "GSH0039" && LineOf(d) == 299);
        Assert.DoesNotContain(splitBoth.Analyzer, static d => d.Id == "GSH0039" && LineOf(d) == 299);

        var pointerPath = RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Core",
            "PrivateFrameTypeKernels.cu.cs");
        var pointerCompilation = RoslynTestUtility.CreateCompilationAt(pointerPath, File.ReadAllText(pointerPath));
        var pointerDefault = await KernelDiagnostics.RunCompilationAsync(pointerCompilation, "Cpu");
        var pointerBoth = await KernelDiagnostics.RunCompilationAsync(
            pointerCompilation, "Cpu", BuildOptions("Stack", "Stack"));

        Assert.Contains(pointerDefault.Analyzer, static d => d.Id == "GSH0041" && LineOf(d) == 207);
        Assert.DoesNotContain(pointerBoth.Analyzer, static d => d.Id == "GSH0041" && LineOf(d) == 207);
    }

    private static async Task<KernelDiagnosticsRun> RunLiftCasesAsync(
        string dialect,
        string? nativeModel,
        string? managedModel,
        string? exportDeviceCode = null)
    {
        var compilation = RoslynTestUtility.CreateCompilation(RoslynTestUtility.ReadTestCase(LiftTestCase));
        var options = BuildOptions(nativeModel, managedModel, exportDeviceCode);
        return await KernelDiagnostics.RunCompilationAsync(compilation, dialect, options);
    }

    private static Dictionary<string, string>? BuildOptions(
        string? nativeModel,
        string? managedModel,
        string? exportDeviceCode = null)
    {
        var options = new Dictionary<string, string>();
        if (nativeModel is not null)
        {
            options["build_property.GSharpNativeBarrierModel"] = nativeModel;
        }

        if (managedModel is not null)
        {
            options["build_property.GSharpManagedBarrierModel"] = managedModel;
        }

        if (exportDeviceCode is not null)
        {
            options["build_property.GSharpExportDeviceCode"] = exportDeviceCode;
        }

        return options.Count == 0 ? null : options;
    }

    private static int LineOf(Diagnostic diagnostic) =>
        diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1;

    private static (int Line, string Id)[] OrderByLine(
        ImmutableArray<Diagnostic> diagnostics,
        params string[] ids) =>
        diagnostics
            .Where(d => ids.Contains(d.Id))
            .OrderBy(static d => LineOf(d))
            .ThenBy(static d => d.Id, StringComparer.Ordinal)
            .Select(static d => (LineOf(d), d.Id))
            .ToArray();
}
