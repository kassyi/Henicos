#nullable enable
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// <c>[GpuReadOnly]</c> promises that no thread writes the array while the kernel runs, through any alias. The analyzer
/// reports, at the write (GSH0118), each write it can see: of the array itself, of a span or array converted from it, and
/// through a method that writes its operand or its parameter. The reads of the same arrays are not reported.
/// </summary>
[Category(TestCategory.Compiler)]
public class ReadOnlyArrayWriteDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ReadOnlyArrayWrites.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ElementWrites_ParameterAndField_AreReportedAtTheElement(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // The assignment, the field, ++, -- and the compound assignment; h.Free and the reads are not reported
        Assert.Equal(
            [(Analyzer, 18, 9, 18, 13), (Analyzer, 19, 9, 19, 19), (Analyzer, 22, 9, 22, 13)],
            diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id, ReadOnlyArrayWriteAnalysis.ElementWrite)
                .Where(static r => r.StartLine < 25));
        Assert.Equal(
            [(Analyzer, 20, 9, 20, 13), (Analyzer, 21, 11, 21, 15)],
            diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id, ReadOnlyArrayWriteAnalysis.ElementUpdate));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ByReference_ReadOnlyElement_IsReportedAtTheElement(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // Increment(ref o[0]) on the next line is not reported
        Assert.Equal(
            [(Analyzer, 30, 23, 30, 27)],
            diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id, ReadOnlyArrayWriteAnalysis.ByReference));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConvertedArrayAndSpans_WriteThroughTheAlias_AreReportedAtTheElement(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // x = h.Fixed, view = r.AsSpan() and sliced = Gpu.NotNull(r).AsSpan().Slice(1); own = o is not read-only
        Assert.Equal(
            [(Analyzer, 38, 9, 38, 13), (Analyzer, 40, 9, 40, 16), (Analyzer, 42, 9, 42, 18)],
            diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id, ReadOnlyArrayWriteAnalysis.ElementWrite)
                .Where(static r => r.StartLine is >= 36 and <= 46));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task WritingMethods_ReadOnlyOperand_AreReportedAtTheCall(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // Fill, Array.Fill, Array.Copy into it, CopyTo into it and Array.Reverse; CopyTo out of it and Array.Fill(o) are not
        Assert.Equal(
            [
                (Analyzer, 51, 9, 51, 28),
                (Analyzer, 52, 9, 52, 26),
                (Analyzer, 53, 9, 53, 28),
                (Analyzer, 54, 9, 54, 38),
                (Analyzer, 56, 9, 56, 25)
            ],
            diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id, ReadOnlyArrayWriteAnalysis.WritingMethod));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DeviceFunctionArgument_CalleeWritesTheParameter_IsReportedAtTheArgument(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // WriteFirst(o) and ReadFirst(...) are not reported
        Assert.Equal(
            [(Analyzer, 63, 20, 63, 21), (Analyzer, 64, 20, 64, 27)],
            diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id, ReadOnlyArrayWriteAnalysis.PassedToWriter));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task EveryWrite_TestCase_IsReportedOnceAndNothingElse(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(16, diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id).Count);
    }
}
