#nullable enable
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Cpu.Generator;

/// <summary>
/// Suspending instance <c>[GpuDeviceFunction]</c> methods of a partial struct must not emit invalid <c>static</c>
/// managed state machines (<c>CS0120</c>) in any configuration, and must still report <c>GSH0066</c> once per
/// suspending method at the method identifier even when the analyzer is disabled.
/// </summary>
[Category(TestCategory.Compiler)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class InstanceDeviceFunctionStateMachineTests
{
    private const string Source = """
        using Kassyi.GSharp.Core;

        namespace InstanceBarrierShape;

        public partial struct PartialStructInstanceBarrier
        {
            public int Offset;

            [GpuDeviceFunction]
            public void Step(int i) { Gpu.SyncThreads(); Offset += i; }

            [GpuDeviceFunction]
            public readonly int Read(int i) { Gpu.SyncThreads(); return Offset + i; }

            [GpuDeviceFunction]
            public void Mask() { Offset += (int)(Warp.ActiveMask() & 0xFu); }

            [GpuDeviceFunction]
            public void Plain(int i) { Offset += i; }

            [GpuDeviceFunction]
            public void Twice(int i) { Step(i); this.Step(i); }

            [GpuDeviceFunction]
            public int AddGet(int i) { Offset += i; return Offset; }

            [GpuDeviceFunction]
            public readonly int ReadonlyImplicitThisWithSplit(int v)
            {
                Gpu.SyncThreads();
        #pragma warning disable CS8656 // The implicit copy of this is the behavior under test
                return AddGet(Warp.ShuffleSync(0xFu, v, 0));
        #pragma warning restore CS8656
            }
        }

        public static partial class PartialStructInstanceBarrierKernels
        {
            [GpuKernel]
            public static void CallKernel(int[] data)
            {
                var step = new PartialStructInstanceBarrier();
                step.Step(1);
                step.Plain(2);
                data[ThreadIndex.X] += step.Offset + step.Read(3) + step.ReadonlyImplicitThisWithSplit(4);
            }

            [GpuKernel]
            public static void MaskKernel(int[] data)
            {
                var step = new PartialStructInstanceBarrier();
                step.Mask();
                step.Twice(5);
                data[ThreadIndex.X] += step.Offset;
            }

            [GpuKernel]
            public static void ElementKernel(PartialStructInstanceBarrier[] steps)
            {
                steps[ThreadIndex.X].Step(1);
            }
        }
        """;

    public static TheoryData<string, string?, string?> Configurations() => new()
    {
        { "Cpu", null, null },
        { "Cpu", "build_property.GSharpManagedBarrierModel", "Stack" },
        { "Cpu", "build_property.GSharpNativeBarrierModel", "Stack" },
        { "Cuda", null, null },
    };

    [Theory]
    [MemberData(nameof(Configurations))]
    [Scenario(TestScenario.SadPath)]
    public void SuspendingInstanceMethodsOnPartialStruct_GeneratorRun_EmitsNoStateMachineOrCSharpErrorsAndReportsGsh0066OncePerMethod(
        string dialect,
        string? optionKey,
        string? optionValue)
    {
        var compilation = RoslynTestUtility.CreateCompilation(Source);
        Dictionary<string, string>? options = optionKey is null || optionValue is null
            ? null
            : new Dictionary<string, string> { [optionKey] = optionValue };

        var driver = CSharpGeneratorDriver.Create(
                generators: [new GpuKernelGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options,
                optionsProvider: new TestAnalyzerConfigOptionsProvider(dialect, options))
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

        Assert.Null(driver.GetRunResult().Results.Single().Exception);

        var csharpErrors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("CS", StringComparison.Ordinal))
            .Select(static d => $"{d.Id}: {d.GetMessage()} @ {d.Location.GetLineSpan().Path}:{d.Location.GetLineSpan().StartLinePosition.Line + 1}")
            .ToArray();
        Assert.True(csharpErrors.Length == 0, "Unexpected C# errors:\n" + string.Join("\n", csharpErrors));

        foreach (var tree in output.SyntaxTrees.Skip(compilation.SyntaxTrees.Length))
        {
            var root = tree.GetRoot();
            Assert.DoesNotContain(
                root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>(),
                static type => type.Identifier.ValueText.StartsWith("__gsharp_frame_", StringComparison.Ordinal));
            Assert.DoesNotContain(
                root.DescendantNodes().OfType<MethodDeclarationSyntax>(),
                static method =>
                    method.Identifier.ValueText.StartsWith("__gsharp_statemachine_", StringComparison.Ordinal) &&
                    method.Identifier.ValueText.Contains("PartialStructInstanceBarrier__", StringComparison.Ordinal));
        }

        Assert.Empty(generatorDiagnostics);

        var stated = KernelDiagnostics.FindStatedFailures(
                new KernelDiagnosticsRun(compilation, output, ImmutableArray<Diagnostic>.Empty, generatorDiagnostics))
            .OrderBy(static f => f.Failure, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                ("(10,17): GSH0066", false),
                ("(13,25): GSH0066", false),
                ("(16,17): GSH0066", false),
                ("(22,17): GSH0066", false),
                ("(28,25): GSH0066", false),
            ],
            stated);

        var generatedText = string.Join(
            "\n",
            output.SyntaxTrees.Skip(compilation.SyntaxTrees.Length).Select(static t => t.GetText().ToString()));
        Assert.Contains(GpuCodeConstraints.InstanceSuspendingReason, generatedText, StringComparison.Ordinal);
        Assert.DoesNotContain(GSharpDiagnostics.UnsupportedSyntax.Id, generatedText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingInstanceMethodsOnPartialStruct_WithAnalyzer_ReportsOnlyGsh0066WithFullParity(
        string dialect)
    {
        var run = await KernelDiagnostics.RunCompilationAsync(RoslynTestUtility.CreateCompilation(Source), dialect);

        Assert.Empty(run.Generator);
        Assert.Empty(run.Analyzer.Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id));

        var statedWithAnalyzer = KernelDiagnostics.FindStatedFailures(run)
            .OrderBy(static f => f.Failure, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(5, statedWithAnalyzer.Length);
        Assert.All(statedWithAnalyzer, static f =>
        {
            Assert.Contains(GSharpDiagnostics.InstanceDeviceFunctionNotSupported.Id, f.Failure, StringComparison.Ordinal);
            Assert.True(f.IsReported);
        });
    }
}
