#nullable enable
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0118 for a variable or struct given a value whose content the analysis does not read: an array element, a call,
/// a conditional, an <c>out</c> or <c>ref</c> argument, a mutable <c>ref</c> local. Such a slot holds what its
/// declaration says, so a write through a <c>[GpuReadOnly]</c> field of it is reported, unless the body gives the
/// field another array. A <c>ref readonly</c> local and an <c>in</c> argument cannot write and change nothing.
/// </summary>
[Category(TestCategory.Compiler)]
public class ReadOnlyArrayWriteUnreadSourceDiagnosticsTests
{
    private const string TestCase = "Diagnostics/ReadOnlyArrayWriteUnreadSources.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ArrayElementStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(diagnostics, "UnreadableStructArrayElementKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task CallResultStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(diagnostics, "UnreadableStructFromCallKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task StructParameter_ReassignedFromCall_ReadOnlyFieldWriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(diagnostics, "ReassignedStructParamFromCallKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ArrayElementStruct_NestedReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 24)],
            InKernel(diagnostics, "NestedUnreadableStructArrayElementKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReinitializedStruct_ReadOnlyArrayAssignedAfter_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // A struct reset to its default value holds null, which does not hide the read-only array given after it
        Assert.Equal(
            [(Analyzer, 5, 9, 5, 18)],
            InKernel(diagnostics, "ReinitializedStructThenReadOnlyKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DefaultAssignedStruct_ReadOnlyArrayAssignedAfter_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 5, 9, 5, 18)],
            InKernel(diagnostics, "DefaultAssignedStructThenReadOnlyKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReinitializedNestedStruct_ReadOnlyArrayAssignedAfter_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 5, 9, 5, 24)],
            InKernel(
                diagnostics,
                "ReinitializedNestedStructThenReadOnlyKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task OutStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(diagnostics, "OutStructReadOnlyFieldKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task OutField_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(diagnostics, "OutReadOnlyFieldKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefArgumentStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 19)],
            InKernel(diagnostics, "RefArgumentStructReadOnlyFieldKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ConditionalStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(diagnostics, "ConditionalStructReadOnlyFieldKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DeconstructedUnreadStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 19)],
            InKernel(diagnostics, "DeconstructedUnreadStructKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ArrayElementStruct_Copied_ReadOnlyFieldWriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 22)],
            InKernel(diagnostics, "UnreadableStructCopiedKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NestedField_GivenCallResult_ReadOnlyFieldWriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 24)],
            InKernel(diagnostics, "NestedFieldUnreadableStructKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefReadOnlyLocal_NestedStructWriteThroughOriginal_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // A ref readonly alias cannot write, so w.Inner keeps r
        Assert.Equal(
            [(Analyzer, 5, 9, 5, 24)],
            InKernel(
                diagnostics,
                "RefReadOnlyLocalNestedStructWriteThroughOriginalKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefReadOnlyLocal_ReadOnlyArrayWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 13)],
            InKernel(diagnostics, "RefReadOnlyLocalReadOnlyArrayWriteKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_StructWithReadOnlyField_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(
                diagnostics,
                "RefLocalStructWithReadOnlyFieldUnreassignedKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task RefLocal_StructWithReadOnlyFieldGivenWritable_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // h.Fixed[0] writes o here, but the analysis does not pair a ref local with the variable it aliases: h, given
        // an unread value through ref, holds what its [GpuReadOnly] field declares, so the write is reported to stay
        // safe. Pairing a ref local with its target, for #2177 (GSH0118 is not reported for a write through a ref
        // local that aliases a [GpuReadOnly] array or a struct field holding one), turns this into a write that is
        // not reported.
        Assert.Equal(
            [(Analyzer, 4, 9, 4, 19)],
            InKernel(
                diagnostics,
                "RefLocalStructWithReadOnlyFieldReassignedViaAliasKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task InArgument_NestedStructField_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // An in argument cannot write, so w.Inner keeps r
        Assert.Equal(
            [(Analyzer, 5, 9, 5, 24)],
            InKernel(diagnostics, "InArgumentKeepsSourcesKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task LocalFunction_GivesReadOnlyArray_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 24)],
            InKernel(diagnostics, "LocalFunctionAssignsReadOnlyKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ArrayElementStruct_WritableFieldWrite_IsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "UnreadStructWritableFieldKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ArrayElementStruct_ReadOnlyFieldGivenWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "UnreadableStructOverwrittenWritableFieldKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task RefLocal_NestedStructFieldGivenWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        // A mutable ref to w.Inner may give it any value, so w.Inner.Data holds what its declaration says
        Assert.Empty(InKernel(diagnostics, "RefLocalNestedStructReassignedWritableKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task RefLocal_NestedArrayFieldGivenWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "RefLocalNestedArrayFieldReassignedWritableKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task RefLocal_StructGivenWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "RefLocalSingleLevelStructReassignedWritableKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task RefReassignment_NestedStructFieldGivenWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "RefReassignmentNestedStructReassignedWritableKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ConditionalRef_NestedStructFieldGivenWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "ConditionalRefStructReassignedWritableKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task RefLocal_ArrayVariableGivenWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "RefLocalArrayReassignedWritableKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task LocalFunction_GivesWritableArray_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "LocalFunctionAssignsWritableKernel"));
        Assert.Empty(InKernel(diagnostics, "LocalFunctionRefParameterKernel"));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task EveryWrite_TestCase_IsReportedOnceAndNothingElse(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(TestCase, dialect);

        Assert.Equal(20, diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id).Count);
    }

    private static IReadOnlyList<(DiagnosticOrigin Origin, int StartLine, int StartColumn, int EndLine, int EndColumn)>
        InKernel(ImmutableArray<ReportedDiagnostic> diagnostics, string method, string? reason = null) =>
        ReadOnlyArrayWriteDiagnosticsTests.InKernel(diagnostics, method, reason, TestCase);
}
