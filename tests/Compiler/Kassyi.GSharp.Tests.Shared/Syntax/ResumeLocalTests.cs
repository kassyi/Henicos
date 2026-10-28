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
    public void Test_SpanUsedOnlyBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.SpanBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SpanUsedOnlyOnTheOtherBranch_KeepsItsMemory() =>
        ResumeLocalScenarios.SpanInOtherBranch(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_InitializerReadsEarlierLocal_RunsWhereDeclared() =>
        ResumeLocalScenarios.ViewAfterLocal(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocInLoop_StartsZeroedEveryIteration() =>
        ResumeLocalScenarios.StackAllocInLoop(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocOfConstLocalSize_KeepsItsMemory() =>
        ResumeLocalScenarios.ConstSize(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocPointer_CanBeIncremented() =>
        ResumeLocalScenarios.MovingPointer(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocPointerBeforeBarrier_CanBeIncremented() =>
        ResumeLocalScenarios.MovingPointerBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ReadOnlyStackAllocWithElements_KeepsItsValues() =>
        ResumeLocalScenarios.ReadOnlyElements(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SpanInFlattenedSiblingScope_KeepsItsMemory() =>
        ResumeLocalScenarios.SiblingScopeSpan(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SameNameSpansInSiblingScopes_KeepTheirMemory() =>
        ResumeLocalScenarios.SameNameSpans(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocPointerUsedOnlyBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.PointerBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_LengthOfConstExpressionSize_IsItsValue() =>
        ResumeLocalScenarios.ConstSizeLength(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ZeroLengthStackAllocPointer_Compiles() =>
        ResumeLocalScenarios.ZeroLengthPointer(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ZeroLengthStackAllocPointerBeforeBarrier_Compiles() =>
        ResumeLocalScenarios.ZeroLengthPointerBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_StackAllocPointerWithElements_KeepsItsValues() =>
        ResumeLocalScenarios.PointerElements(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocPointerWithoutElements_StartsZeroed() =>
        ResumeLocalScenarios.PointerZeroed(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ZeroLengthStackAllocSpan_Compiles() =>
        ResumeLocalScenarios.ZeroLengthSpan(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ZeroLengthStackAllocSpanBeforeBarrier_Compiles() =>
        ResumeLocalScenarios.ZeroLengthSpanBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocPointerCapturedByDelegateRunBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.LambdaBeforeBarrier(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_StackAllocPointerCapturedByStackLinqLambdaBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.StackLinqBeforeBarrier(s_runNative);
}
