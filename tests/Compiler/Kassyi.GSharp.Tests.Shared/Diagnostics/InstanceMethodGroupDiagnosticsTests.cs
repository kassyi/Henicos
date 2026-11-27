#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Compiler.Transpiler;
using Kassyi.GSharp.Tests.Shared.Helpers;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// A method group of a struct instance <c>[GpuDeviceFunction]</c> (delegate assignment, StackLinq stage, bare or
/// parenthesized) is reported with <c>GSH0003</c> at the group span by both analyzer and generator, while invoked
/// parenthesized calls, reduced extension method groups, and <c>nameof</c> are not.
/// </summary>
[Category(TestCategory.Compiler)]
public class InstanceMethodGroupDiagnosticsTests
{
    private const string ExpectedGsh0003Message =
        "The syntax 'method group of an instance method of a struct (the delegate would hold a boxed copy of the receiver)' is not supported in GSharp GPU kernels";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task InstanceDeviceFunction_MethodGroup_ReportsGsh0003AtGroupInAnalyzerAndGenerator(string dialect)
    {
        const string source = """
            using System;
            using Kassyi.GSharp.Core;
            using Kassyi.ZeroAlloc.Linq;

            namespace InstanceMethodGroups;

            public partial struct Counter
            {
                public int X;

                [GpuDeviceFunction]
                public int Get() => X;

                [GpuDeviceFunction]
                public int Map(int v) => X + v;

                [GpuDeviceFunction]
                public int BareGroup()
                {
                    Func<int> f = Get;
                    return f();
                }
            }

            public static partial class Kernels
            {
                [GpuKernel]
                public static void GroupKernel(int[] data, int[] r)
                {
                    var s = new Counter { X = 3 };
                    Func<int> f = s.Get;
                    Func<int> g = ((s.Get));
                    int linq = data.AsStackEnumerable().Select(_ => s.Get()).Sum();
                    Func<int, int> stage = _ => 0;
                    int stageGroup = data.AsStackEnumerable().Select(s.Map).Sum();
                    r[ThreadIndex.X] = f() + g() + linq + stage(0) + stageGroup + s.BareGroup();
                }
            }
            """;

        var run = await KernelDiagnostics.RunCompilationAsync(RoslynTestUtility.CreateCompilation(source), dialect);

        var analyzerGsh0003 = run.Analyzer
            .Where(static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id)
            .Select(static d => ReportedDiagnostic.From(Analyzer, d))
            .Where(static d => d.Message == ExpectedGsh0003Message)
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ToArray();

        Assert.Equal(4, analyzerGsh0003.Length);
        Assert.Equal(
            [
                (Analyzer, 20, 23, 20, 26),
                (Analyzer, 31, 23, 31, 28),
                (Analyzer, 32, 25, 32, 30),
                (Analyzer, 35, 58, 35, 63),
            ],
            analyzerGsh0003.Select(static d => (d.Origin, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn)));

        var stated = KernelDiagnostics.FindStatedFailures(run)
            .Where(static f => f.Failure.Contains(GSharpDiagnostics.UnsupportedSyntax.Id, StringComparison.Ordinal))
            .OrderBy(static f => f.Failure, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(4, stated.Length);
        Assert.All(stated, static f => Assert.True(f.IsReported));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task InstanceDeviceFunction_ParenthesizedCallExtensionGroupAndNameof_ReportNoGsh0003(string dialect)
    {
        const string source = """
            using System;
            using Kassyi.GSharp.Core;

            namespace InstanceMethodGroupControls;

            public partial struct Counter
            {
                public int X;

                [GpuDeviceFunction]
                public int Get() => X;
            }

            public static class CounterExtensions
            {
                [GpuDeviceFunction]
                public static int ExtGet(this in Counter c) => c.X;
            }

            public static partial class Kernels
            {
                [GpuKernel]
                public static void ControlKernel(int[] r)
                {
                    var s = new Counter { X = 3 };
                    int direct = ((s.Get))();
                    Func<int> ext = s.ExtGet;
                    int nameLen = nameof(s.Get).Length;
                    r[ThreadIndex.X] = direct + ext() + nameLen;
                }
            }
            """;

        var run = await KernelDiagnostics.RunCompilationAsync(RoslynTestUtility.CreateCompilation(source), dialect);

        Assert.DoesNotContain(run.Analyzer, static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
        Assert.DoesNotContain(run.Generator, static d => d.Id == GSharpDiagnostics.UnsupportedSyntax.Id);
    }
}
