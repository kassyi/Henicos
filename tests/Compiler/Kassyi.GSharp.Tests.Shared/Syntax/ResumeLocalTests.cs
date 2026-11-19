using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A ref struct local or stackalloc pointer that is not live across a barrier keeps its memory on the native CPU
/// dialect: it is declared again on every resume, not copied into the frame.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class ResumeLocalTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocSpan_UsedOnlyBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.SpanBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocSpan_UsedOnlyOnOtherBranch_KeepsItsMemory() =>
        ResumeLocalScenarios.SpanInOtherBranch(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalInitializer_ReadsEarlierLocal_RunsWhereDeclared() =>
        ResumeLocalScenarios.ViewAfterLocal(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocInLoop_LoopIteration_StartsZeroedEveryIteration() =>
        ResumeLocalScenarios.StackAllocInLoop(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocConstSize_ConstLocalSize_KeepsItsMemory() =>
        ResumeLocalScenarios.ConstSize(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_PointerIncrement_CanBeIncremented() =>
        ResumeLocalScenarios.MovingPointer(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_IncrementBeforeBarrier_CanBeIncremented() =>
        ResumeLocalScenarios.MovingPointerBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ReadOnlyStackAlloc_WithElements_KeepsItsValues() =>
        ResumeLocalScenarios.ReadOnlyElements(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocSpan_FlattenedSiblingScope_KeepsItsMemory() =>
        ResumeLocalScenarios.SiblingScopeSpan(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SameNameSpans_SiblingScopes_KeepTheirMemory() =>
        ResumeLocalScenarios.SameNameSpans(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_UsedOnlyBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.PointerBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConstExpressionSize_LengthEvaluation_EvaluatesToConstValue() =>
        ResumeLocalScenarios.ConstSizeLength(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroLengthStackAllocPointer_Compilation_ExecutesSuccessfully() =>
        ResumeLocalScenarios.ZeroLengthPointer(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroLengthStackAllocPointer_BeforeBarrier_ExecutesSuccessfully() =>
        ResumeLocalScenarios.ZeroLengthPointerBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void StackAllocPointer_InitializedWithElements_KeepsItsValues() =>
        ResumeLocalScenarios.PointerElements(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_WithoutElements_StartsZeroed() =>
        ResumeLocalScenarios.PointerZeroed(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroLengthStackAllocSpan_Compilation_ExecutesSuccessfully() =>
        ResumeLocalScenarios.ZeroLengthSpan(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroLengthStackAllocSpan_BeforeBarrier_ExecutesSuccessfully() =>
        ResumeLocalScenarios.ZeroLengthSpanBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_CapturedByDelegateRunBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.LambdaBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_CapturedByStackLinqLambdaBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.StackLinqBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Lambda_CapturingVariableAcrossBarrier_EvaluatesCorrectly() =>
        ResumeLocalScenarios.LambdaCapturedInt(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalFunction_CapturingVariableAcrossBarrier_EvaluatesCorrectly() =>
        ResumeLocalScenarios.LocalFunctionCapturedInt(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RecursiveLocalFunction_CapturingVariableAcrossBarrier_EvaluatesCorrectly() =>
        ResumeLocalScenarios.RecursiveLocalFuncCapturedInt(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void MutuallyRecursiveLocalFunction_CapturingVariableAcrossBarrier_EvaluatesCorrectly() =>
        ResumeLocalScenarios.MutuallyRecursiveLocalFuncCapturedInt(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void AsymmetricMutuallyRecursiveLocalFunction_CapturingVariableAcrossBarrier_EvaluatesCorrectly() =>
        ResumeLocalScenarios.AsymmetricMutuallyRecursiveLocalFuncCapturedInt(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ReadOnlyCollectionSpan_UsedOnlyBeforeBarrier_KeepsItsValues() =>
        ResumeLocalScenarios.ReadOnlyCollectionSpan(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void EmptyCollectionSpan_BeforeBarrier_HasZeroLength() =>
        ResumeLocalScenarios.EmptyCollectionSpan(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CollectionSpan_UsedOnlyBeforeBarrier_KeepsItsValues() =>
        ResumeLocalScenarios.CollectionSpanBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CollectionSpanInLoop_LoopIteration_ReinitializesEveryIteration() =>
        ResumeLocalScenarios.CollectionSpanInLoop(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CollectionSpan_WithVariableElementBeforeBarrier_KeepsComputedValues() =>
        ResumeLocalScenarios.VariableCollectionSpan(s_runNative);
}
