#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback declares the same locals again on every resume.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class ResumeLocalManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocSpan_ManagedUsedOnlyBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.SpanBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocSpan_ManagedUsedOnlyOnOtherBranch_KeepsItsMemory() =>
        ResumeLocalScenarios.SpanInOtherBranch(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalInitializer_ManagedReadsEarlierLocal_RunsWhereDeclared() =>
        ResumeLocalScenarios.ViewAfterLocal(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocInLoop_ManagedLoopIteration_StartsZeroedEveryIteration() =>
        ResumeLocalScenarios.StackAllocInLoop(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocConstSize_ManagedConstLocalSize_KeepsItsMemory() =>
        ResumeLocalScenarios.ConstSize(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_ManagedPointerIncrement_CanBeIncremented() =>
        ResumeLocalScenarios.MovingPointer(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_ManagedIncrementBeforeBarrier_CanBeIncremented() =>
        ResumeLocalScenarios.MovingPointerBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ReadOnlyStackAlloc_ManagedWithElements_KeepsItsValues() =>
        ResumeLocalScenarios.ReadOnlyElements(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocSpan_ManagedFlattenedSiblingScope_KeepsItsMemory() =>
        ResumeLocalScenarios.SiblingScopeSpan(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SameNameSpans_ManagedSiblingScopes_KeepTheirMemory() =>
        ResumeLocalScenarios.SameNameSpans(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_ManagedUsedOnlyBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.PointerBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConstExpressionSize_ManagedLengthEvaluation_EvaluatesToConstValue() =>
        ResumeLocalScenarios.ConstSizeLength(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroLengthStackAllocPointer_ManagedInvocation_ExecutesSuccessfully() =>
        ResumeLocalScenarios.ZeroLengthPointer(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroLengthStackAllocPointer_ManagedBeforeBarrier_ExecutesSuccessfully() =>
        ResumeLocalScenarios.ZeroLengthPointerBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void StackAllocPointer_ManagedInitializedWithElements_KeepsItsValues() =>
        ResumeLocalScenarios.PointerElements(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_ManagedWithoutElements_StartsZeroed() =>
        ResumeLocalScenarios.PointerZeroed(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroLengthStackAllocSpan_ManagedInvocation_ExecutesSuccessfully() =>
        ResumeLocalScenarios.ZeroLengthSpan(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ZeroLengthStackAllocSpan_ManagedBeforeBarrier_ExecutesSuccessfully() =>
        ResumeLocalScenarios.ZeroLengthSpanBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_ManagedCapturedByDelegateRunBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.LambdaBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void StackAllocPointer_ManagedCapturedByStackLinqLambdaBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.StackLinqBeforeBarrier(s_runManaged);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
