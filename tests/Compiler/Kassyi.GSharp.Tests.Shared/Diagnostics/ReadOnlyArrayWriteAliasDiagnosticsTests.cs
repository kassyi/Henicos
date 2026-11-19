#nullable enable
using System.Collections.Immutable;
using Kassyi.GSharp.Compiler.Diagnostics.Kernel.Rules.ReadOnlyArrays;
using static Kassyi.GSharp.Tests.Shared.Helpers.DiagnosticOrigin;

namespace Kassyi.GSharp.Tests.Shared.Diagnostics;

/// <summary>
/// GSH0118 through the variables that hold a <c>[GpuReadOnly]</c> array: nested struct fields, tuple deconstruction,
/// struct copies and object initializers. A write is reported when every value the body gives the variable is a
/// read-only array, and not when one of them is another array. The limits of that analysis are in
/// <c>ReadOnlyArrayWriteLimits.cu.cs</c>.
/// </summary>
[Category(TestCategory.Compiler)]
public class ReadOnlyArrayWriteAliasDiagnosticsTests
{
    private const string AliasTestCase = "Diagnostics/ReadOnlyArrayWriteAliases.cu.cs";
    private const string LimitsTestCase = "Diagnostics/ReadOnlyArrayWriteLimits.cu.cs";

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task NestedStructField_HoldsTheArray_WriteThroughTheFieldIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        // w.Inner.Data = r and new Outer { Inner = new Inner { Data = r } }
        Assert.Equal(
            [(Analyzer, 4, 9, 4, 31), (Analyzer, 6, 9, 6, 34)],
            InKernel(diagnostics, "NestedStructAliasKernel", ReadOnlyArrayWriteAnalysis.ElementWrite, AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task TupleDeconstruction_ReadOnlyElement_WriteThroughTheVariableIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        // var (p, q) = (r, o); (uint[] p2, var q2) = (r, o); var (a, (b, c)) = (o, (r, o)); q[0] is not reported
        Assert.Equal(
            [(Analyzer, 3, 9, 3, 13), (Analyzer, 6, 9, 6, 14), (Analyzer, 8, 9, 8, 13)],
            InKernel(
                diagnostics,
                "TupleDeconstructionAliasKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReassignedNestedStruct_GivenNewInnerStruct_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        // w.Inner = new Holder() resets w.Inner.Data to null, which is neither read-only nor another array, so the
        // write is decided by the earlier w.Inner.Data = r. At run time the write throws on null, so no running
        // code is rejected; ignoring the reset keeps the write after a reset and a new read-only array reported.
        Assert.Equal(
            [(Analyzer, 5, 9, 5, 24)],
            InKernel(diagnostics, "ReassignedNestedStructKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task TupleDeconstruction_NonTupleLiteralRightSide_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(LimitsTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "DeconstructCallKernel", testCase: LimitsTestCase));
        Assert.Empty(InKernel(diagnostics, "DeconstructCallReassignedKernel", testCase: LimitsTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task TupleDeconstruction_WritableSwap_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "SwapWritableTupleKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task SelfAssignedStructField_Writable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "SelfAssignedFieldKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReassignedNestedStruct_ObjectInitializerOmittingField_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        // The field the initializer does not name holds null after the reset; as above, the write would throw on
        // null, so reporting it rejects no running code
        Assert.Equal(
            [(Analyzer, 5, 9, 5, 24)],
            InKernel(diagnostics, "InvalidateParentStructSiblingInitKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
        Assert.Equal(
            [(Analyzer, 5, 9, 5, 24)],
            InKernel(diagnostics, "InvalidateParentStructEmptyInitKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ReassignedNestedStruct_CompoundAssignment_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(LimitsTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "StructCompoundAssignmentKernel", testCase: LimitsTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task TupleDeconstruction_WritableParamSwappedToLocal_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "TupleSwapWritableParamToLocalKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task TupleDeconstruction_LocalReadOnlySwappedWithWritableParam_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "TupleSwapLocalReadOnlyWithWritableParamKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task TupleDeconstruction_NestedSwapWithWritableParams_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "NestedTupleSwapWritableParamsKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task TupleDeconstruction_LocalReadOnlySwappedWithParamField_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "TupleSwapLocalReadOnlyWithParamFieldKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task StructParameterNestedField_ConditionalReadOnlyAssign_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "ConditionalParamFieldAssignKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task StructParameterNestedField_ConditionalReadOnlyInitializer_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "ConditionalParamFieldInitKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task WritableParameter_ConditionalReadOnlyAssign_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "ConditionalArrayParamAssignKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task WritableParameter_CapturedBeforeReadOnlyReassign_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "ReadParamBeforeOverwriteKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ReadOnlyStructField_ConditionalWritableAssign_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(
            InKernel(diagnostics, "ConditionalReadOnlyStructFieldToWritableKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task LocalStructCopy_FromParamConditionalReadOnlyAssign_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(
            InKernel(diagnostics, "LocalStructCopyFromParamConditionalAssignKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReadOnlyParameter_ReassignedAnotherReadOnlyArray_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 13)],
            InKernel(
                diagnostics,
                "ReadOnlyParamReassignedToReadOnlyKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReadOnlyStructField_ReassignedReadOnlyArray_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(
                diagnostics,
                "ReadOnlyStructFieldReassignedToReadOnlyKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ParameterReassignment_FlowInsensitiveLimit_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(LimitsTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "TupleSwapReadOnlyIntoParamWriteKernel", testCase: LimitsTestCase));
        Assert.Empty(
            InKernel(diagnostics, "TupleSwapLocalReadOnlyIntoParamFieldWriteKernel", testCase: LimitsTestCase));
        Assert.Empty(InKernel(diagnostics, "TupleSwapParamReceivesLocalReadOnlyWriteKernel", testCase: LimitsTestCase));
        Assert.Empty(InKernel(diagnostics, "NestedStructParamDirectAliasKernel", testCase: LimitsTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task CtorInitOmitsField_ConditionalReadOnlyAssign_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "ExplicitCtorWithObjectInitializerKernel", testCase: AliasTestCase));
        Assert.Empty(
            InKernel(diagnostics, "ExplicitParameterlessCtorWithObjectInitializerKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task NestedCtorInitOmitsField_ConditionalReadOnlyAssign_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "NestedExplicitCtorWithObjectInitializerKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task CtorInitAssignsReadOnlyField_Write_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 22)],
            InKernel(
                diagnostics,
                "ExplicitCtorWithObjectInitializerReadOnlyFieldKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task LocalCopyReadOnlyField_ReassignedWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(
            InKernel(diagnostics, "LocalReadOnlyFieldStructCopyReassignedWritableKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ReinitializedReadOnlyFieldStruct_FieldNotNamed_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        // h = new Holder { Free = o } leaves the [GpuReadOnly] parameter field to its declaration, and the local reset
        // to a default value gives Fixed null, so the earlier Fixed = r decides. Both writes would throw on null, so
        // reporting them rejects no running code.
        Assert.Equal(
            [(Analyzer, 6, 9, 6, 19), (Analyzer, 10, 9, 10, 29)],
            InKernel(diagnostics, "ReinitializedReadOnlyFieldStructKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task OutVarParam_ConditionalReadOnlyAssign_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "OutVarDeclarationConditionalReadOnlyKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task OutReadOnlyParameter_GivenWritableArray_WriteIsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        // The value an out argument receives is not read, so the [GpuReadOnly] declaration of r decides, as for a
        // field; r[0] writes o here, but the analysis cannot tell that apart from a callee that gives r back
        Assert.Equal(
            [(Analyzer, 3, 9, 3, 13)],
            InKernel(diagnostics, "OutReadOnlyParamReassignedWritableKernel", ReadOnlyArrayWriteAnalysis.ElementWrite));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task ParenthesizedLValue_ReassignedWritable_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "ParenthesizedMemberReassignedWritableKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ParenthesizedLValue_AssignedReadOnly_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 24)],
            InKernel(
                diagnostics,
                "ParenthesizedMemberAssignedReadOnlyKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task CopiedStructParameter_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 23)],
            InKernel(
                diagnostics,
                "CopiedReadOnlyFieldStructParamKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task CopiedLocalStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 22)],
            InKernel(
                diagnostics,
                "CopiedLocalReadOnlyFieldStructKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task CopiedNestedStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 23)],
            InKernel(
                diagnostics,
                "CopiedNestedReadOnlyFieldStructKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task DeconstructedStructCopy_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 3, 9, 3, 19)],
            InKernel(
                diagnostics,
                "DeconstructedReadOnlyFieldStructCopyKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.SadPath)]
    public async Task ChainedCopiedStruct_ReadOnlyFieldWrite_IsReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(
            [(Analyzer, 4, 9, 4, 19)],
            InKernel(
                diagnostics,
                "ChainedCopiedReadOnlyFieldStructKernel",
                ReadOnlyArrayWriteAnalysis.ElementWrite,
                AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task CopiedStruct_WritableField_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "CopiedWritableStructFieldKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task CopiedStruct_OverwrittenField_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(InKernel(diagnostics, "CopiedStructOverwrittenFieldKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task DefaultConstructedStruct_OmittedReadOnlyField_WriteIsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Empty(
            InKernel(diagnostics, "DefaultConstructedStructWithOmittedReadOnlyFieldKernel", testCase: AliasTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task RefLocal_WriteThroughAlias_IsNotReported(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(LimitsTestCase, dialect);

        // A limit: the write through i or d is not followed back to w.Inner or a, which hold r
        Assert.Empty(InKernel(diagnostics, "RefLocalStructAliasWriteKernel", testCase: LimitsTestCase));
        Assert.Empty(InKernel(diagnostics, "RefLocalArrayAliasWriteKernel", testCase: LimitsTestCase));
    }

    [Theory]
    [InlineData("Cpu")]
    [InlineData("Cuda")]
    [Scenario(TestScenario.Boundary)]
    public async Task EveryWrite_AliasTestCase_IsReportedOnceAndNothingElse(string dialect)
    {
        var diagnostics = await KernelDiagnostics.RunTestCaseAsync(AliasTestCase, dialect);

        Assert.Equal(20, diagnostics.Ranges(GSharpDiagnostics.ReadOnlyArrayWritten.Id).Count);
    }

    private static IReadOnlyList<(DiagnosticOrigin Origin, int StartLine, int StartColumn, int EndLine, int EndColumn)>
        InKernel(
            ImmutableArray<ReportedDiagnostic> diagnostics,
            string method,
            string? reason = null,
            string testCase = AliasTestCase) =>
        ReadOnlyArrayWriteDiagnosticsTests.InKernel(diagnostics, method, reason, testCase);
}
