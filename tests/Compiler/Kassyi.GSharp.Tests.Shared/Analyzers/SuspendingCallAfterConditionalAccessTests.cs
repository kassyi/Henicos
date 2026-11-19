using System;
using System.Globalization;
using System.Threading.Tasks;
using Kassyi.GSharp.Compiler.Diagnostics;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Analyzers;

/// <summary>
/// A call that suspends at a barrier inside a null-conditional access (an index, an argument, the right side of an
/// assignment, an inner access) is GSH0062 "after '?.'" from the barrier analyzer; a suspending receiver, a suspending
/// call next to the access and one in another arm of <c>?:</c> report nothing.
/// </summary>
public class SuspendingCallAfterConditionalAccessTests : TranspilerTestBase
{
    private const string TestCase = "Analyzers/BarrierUsage/SuspendingCallAfterConditionalAccessKernels.cu.cs";

    private static readonly BarrierUsageAnalyzer s_analyzer = new();

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task SuspendingCallAfterConditionalAccess_InTheAccess_ReportsGSH0062OnlyThere(string dialect)
    {
        var actual = await RunAnalyzerAsync(TestCase, dialect);

        // The forms of AcceptedFormsKernel (line 86 on) report nothing: the list ends at line 80
        var id = GSharpDiagnostics.SuspendingCallInUnsupportedContext.Id;
        Assert.Equal(
            [
                (id, 47, 23, 47, 34),
                (id, 58, 38, 58, 49),
                (id, 59, 17, 59, 28),
                (id, 69, 36, 69, 47),
                (id, 80, 35, 80, 46),
                (id, 80, 69, 80, 80)
            ],
            actual);

        var messages = (await GeneratorTestRunner
                .TestAnalyzer(TestCase, s_analyzer)
                .WithDialect(dialect)
                .GetDiagnosticsAsync())
            .OrderBy(static d => d.Location.SourceSpan.Start)
            .Select(static d => d.GetMessage(CultureInfo.InvariantCulture))
            .ToArray();
        Assert.All(messages[..5], static m => Assert.Contains("after '?.'", m, StringComparison.Ordinal));
        Assert.Contains("as the right operand of '??'", messages[5], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.HappyPath)]
    public async Task SuspendingCallAfterConditionalAccess_AcceptedForms_ReportsNothing(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.DoesNotContain(diagnostics, static d => d.StartLine >= 86);
    }

    private static async Task<IReadOnlyList<(string Id, int StartLine, int StartCol, int EndLine, int EndCol)>>
        RunAnalyzerAsync(string relativeTestCasePath, string dialect)
    {
        var diagnostics = await GeneratorTestRunner
            .TestAnalyzer(relativeTestCasePath, s_analyzer)
            .WithDialect(dialect)
            .GetDiagnosticsAsync();

        return diagnostics
            .Select(static d => ReportedDiagnostic.From(DiagnosticOrigin.Analyzer, d))
            .Select(static d => (d.Id, d.StartLine, d.StartColumn, d.EndLine, d.EndColumn))
            .OrderBy(static d => d.StartLine)
            .ThenBy(static d => d.StartColumn)
            .ToList();
    }
}
