using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kassyi.GSharp.Compiler;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

[Category(TestCategory.Compiler)]
public class InstanceDeviceFunctionAnalyzerTests : TranspilerTestBase
{
    private const string GSH0066 = "GSH0066";
    private static readonly GpuKernelAnalyzer s_analyzer = new();

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public async Task InstanceDeviceFunction_InstanceMethod_ReportsGSH0066()
    {
        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer("Analyzers/InstanceDeviceFunction_SadPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        var reported = diagnostics.Where(d => d.Id == GSH0066).ToList();

        var single = Assert.Single(reported);
        Assert.Equal(DiagnosticSeverity.Error, single.Severity);
        Assert.Contains("'C.Add'", single.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(GpuCodeConstraints.InstanceOnReferenceTypeReason, single.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(reported, d => d.GetMessage().Contains("'S.Twice'", StringComparison.Ordinal));

        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext("Analyzers/InstanceDeviceFunction_SadPath.cs");
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task InstanceDeviceFunction_StaticDeviceFunctionAndHostInstanceMethod_AllowsBothWithoutDiagnostics()
    {
        var (compilation, _, _) = RoslynTestUtility.GetTestCaseContext("Analyzers/InstanceDeviceFunction_HappyPath.cs");
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer("Analyzers/InstanceDeviceFunction_HappyPath.cs", s_analyzer)
            .GetDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == GSH0066);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task InstanceDeviceFunction_NonSuspendingStructVariants_ReportsNoDiagnostics(string dialect)
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace StructVariants;

            public partial struct PlainStruct
            {
                public int X;

                [GpuDeviceFunction]
                public void Add(int v) => X += v;

                [GpuDeviceFunction]
                public readonly int Get() => X;
            }

            public readonly partial struct ReadonlyStruct
            {
                public readonly int X;

                public ReadonlyStruct(int x) => X = x;

                [GpuDeviceFunction]
                public int Get() => X;
            }

            public partial record struct RecordStruct(int X)
            {
                [GpuDeviceFunction]
                public void Inc() => X++;
            }

            public readonly partial record struct ReadonlyRecordStruct(int X)
            {
                [GpuDeviceFunction]
                public int Doubled() => X * 2;
            }

            public ref partial struct RefStruct
            {
                public int X;

                [GpuDeviceFunction]
                public void Add(int v) => X += v;
            }

            public static partial class Kernels
            {
                [GpuKernel]
                public static void Run(int[] r)
                {
                    var p = new PlainStruct { X = 1 };
                    p.Add(2);
                    var ro = new ReadonlyStruct(3);
                    var rec = new RecordStruct(4);
                    rec.Inc();
                    var roRec = new ReadonlyRecordStruct(5);
                    var rs = new RefStruct { X = 6 };
                    rs.Add(1);
                    r[ThreadIndex.X] = p.Get() + ro.Get() + rec.X + roRec.Doubled() + rs.X;
                }
            }
            """;

        var run = await KernelDiagnostics.RunCompilationAsync(RoslynTestUtility.CreateCompilation(source), dialect);

        Assert.Empty(run.Analyzer.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(run.Generator.Where(static d => d.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task InstanceDeviceFunction_DifferentialKernelsWithAnalyzerEnabled_ReportsNoGsh0066(string dialect)
    {
        var source = File.ReadAllText(RepositoryPaths.Combine(
            "tests", "Compiler", "Kassyi.GSharp.Tests.Shared", "Differential", "InstanceDeviceFunctionKernels.cu.cs"));
        var run = await KernelDiagnostics.RunCompilationAsync(RoslynTestUtility.CreateCompilation(source), dialect);

        Assert.Empty(run.Analyzer.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(run.Generator.Where(static d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(KernelDiagnostics.FindStatedFailures(run));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task InstanceDeviceFunction_UnsupportedForms_ReportsGsh0066WithReasonInAnalyzerAndGenerator(string dialect)
    {
        const string source = """
            using Kassyi.GSharp.Core;

            namespace UnsupportedForms;

            public partial struct SuspendingStruct
            {
                public int X;

                [GpuDeviceFunction]
                public static void SyncHelper() => Gpu.SyncThreads();

                [GpuDeviceFunction]
                public void DirectBarrier() { Gpu.SyncThreads(); X++; }

                [GpuDeviceFunction]
                public int DirectShuffle(int v) => Warp.ShuffleSync(0xFu, v, 0);

                [GpuDeviceFunction]
                public void TransitiveBarrier() => SyncHelper();

                [GpuDeviceFunction]
                public void RecursiveBarrier(int n)
                {
                    if (n > 0)
                    {
                        RecursiveBarrier(n - 1);
                        return;
                    }

                    Gpu.SyncThreads();
                }
            }

            public partial struct GenericBox<T> where T : unmanaged
            {
                public T Value;

                [GpuDeviceFunction]
                public void Set(T v) => Value = v;
            }

            public partial class RefClass
            {
                [GpuDeviceFunction]
                public int Add(int x) => x + 1;
            }

            public static partial class Kernels
            {
                [GpuKernel]
                public static void Call(int[] r)
                {
                    var s = new SuspendingStruct();
                    s.DirectBarrier();
                    s.TransitiveBarrier();
                    r[ThreadIndex.X] = s.DirectShuffle(1);
                }
            }
            """;

        var run = await KernelDiagnostics.RunCompilationAsync(RoslynTestUtility.CreateCompilation(source), dialect);

        var analyzerGsh0066 = run.Analyzer
            .Where(static d => d.Id == GSH0066)
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .OrderBy(static d => d.StartLine)
            .ToArray();

        Assert.Equal(6, analyzerGsh0066.Length);
        Assert.Equal(
            [
                (DiagnosticOrigin.Analyzer, 13, 17, 13, 30),
                (DiagnosticOrigin.Analyzer, 16, 16, 16, 29),
                (DiagnosticOrigin.Analyzer, 19, 17, 19, 34),
                (DiagnosticOrigin.Analyzer, 22, 17, 22, 33),
                (DiagnosticOrigin.Analyzer, 39, 17, 39, 20),
                (DiagnosticOrigin.Analyzer, 45, 16, 45, 19),
            ],
            analyzerGsh0066.Select(static d => (d.Origin, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));

        Assert.Contains(GpuCodeConstraints.InstanceSuspendingReason, analyzerGsh0066[0].Message, StringComparison.Ordinal);
        Assert.Contains(GpuCodeConstraints.InstanceSuspendingReason, analyzerGsh0066[1].Message, StringComparison.Ordinal);
        Assert.Contains(GpuCodeConstraints.InstanceSuspendingReason, analyzerGsh0066[2].Message, StringComparison.Ordinal);
        Assert.Contains(GpuCodeConstraints.InstanceSuspendingReason, analyzerGsh0066[3].Message, StringComparison.Ordinal);
        Assert.Contains(GpuCodeConstraints.InstanceOnGenericStructReason, analyzerGsh0066[4].Message, StringComparison.Ordinal);
        Assert.Contains(GpuCodeConstraints.InstanceOnReferenceTypeReason, analyzerGsh0066[5].Message, StringComparison.Ordinal);

        Assert.Empty(run.Generator);
        Assert.DoesNotContain(
            run.Analyzer,
            static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id &&
                        d.GetMessage().Contains("extension method call of a device function", StringComparison.Ordinal));

        var stated = KernelDiagnostics.FindStatedFailures(run)
            .OrderBy(static f => f.Failure, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(7, stated.Length);
        Assert.Equal(6, stated.Count(static f => f.Failure.Contains(GSH0066, StringComparison.Ordinal)));
        Assert.Equal(1, stated.Count(static f => f.Failure.Contains("(34,34): GSH0003", StringComparison.Ordinal)));
        Assert.All(stated, static f => Assert.True(f.IsReported));
    }
}
