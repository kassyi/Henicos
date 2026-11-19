#nullable enable
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0118 when the sources of a variable form a cycle (a swap of two <c>[GpuReadOnly]</c> arrays, a copy back) or a
/// chain of copies longer than the depth limit of the walk from a write to the array it reaches. The fixed point has
/// already proved that every source is read-only, so a walk that stops at the limit still reports the write.
/// </summary>
[Category(TestCategory.Compiler)]
public class ReadOnlyArrayWriteCycleDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ReadOnlyArrayWriteCycles.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task TemporarySwap_ReadOnlyParameters_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 5, 9, 5, 13)],
            InKernel(diagnostics, "TemporarySwapReadOnlyParamsKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task TupleSwap_ReadOnlyParameters_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 13)],
            InKernel(diagnostics, "TupleSwapReadOnlyParamsKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task CopyBack_ReadOnlyParameter_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 13)],
            InKernel(diagnostics, "CopyBackReadOnlyParamKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task StructParameterSwap_ReadOnlyField_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 5, 9, 5, 20)],
            InKernel(diagnostics, "StructParamSwapKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task SelfAssignment_ReadOnlyParameter_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 13)],
            InKernel(diagnostics, "SelfAssignedReadOnlyParamKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ArrayCopyChain_SeventyLinks_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 73, 9, 73, 15)],
            InKernel(diagnostics, "LongArrayCopyChainKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task StructCopyChain_SeventyLinks_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 73, 9, 73, 20)],
            InKernel(diagnostics, "LongStructCopyChainKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task EveryWrite_TestCase_IsReportedOnceAndNothingElse(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(7, diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id).Count);
    }

    private static IReadOnlyList<(DiagnosticOrigin Origin, int StartLine, int StartColumn, int EndLine, int EndColumn)>
        InKernel(ImmutableArray<ReportedDiagnostic> diagnostics, string method) =>
        ReadOnlyArrayWriteDiagnosticsTests.InKernel(
            diagnostics,
            method,
            ReadOnlyArrayWriteAnalysis.ElementWrite,
            TestCase);
}
