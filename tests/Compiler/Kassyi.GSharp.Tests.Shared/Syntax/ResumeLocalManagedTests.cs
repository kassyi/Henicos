#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

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
    public void Test_Managed_SpanUsedOnlyBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.SpanBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_InitializerReadsEarlierLocal_RunsWhereDeclared() =>
        ResumeLocalScenarios.ViewAfterLocal(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_StackAllocInLoop_StartsZeroedEveryIteration() =>
        ResumeLocalScenarios.StackAllocInLoop(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_StackAllocOfConstLocalSize_KeepsItsMemory() =>
        ResumeLocalScenarios.ConstSize(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_StackAllocPointer_CanBeIncremented() =>
        ResumeLocalScenarios.MovingPointer(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_StackAllocPointerBeforeBarrier_CanBeIncremented() =>
        ResumeLocalScenarios.MovingPointerBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ReadOnlyStackAllocWithElements_KeepsItsValues() =>
        ResumeLocalScenarios.ReadOnlyElements(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SpanInFlattenedSiblingScope_KeepsItsMemory() =>
        ResumeLocalScenarios.SiblingScopeSpan(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SameNameSpansInSiblingScopes_KeepTheirMemory() =>
        ResumeLocalScenarios.SameNameSpans(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_StackAllocPointerUsedOnlyBeforeBarrier_KeepsItsMemory() =>
        ResumeLocalScenarios.PointerBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_LengthOfConstExpressionSize_IsItsValue() =>
        ResumeLocalScenarios.ConstSizeLength(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ZeroLengthStackAllocPointer_Runs() =>
        ResumeLocalScenarios.ZeroLengthPointer(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ZeroLengthStackAllocPointerBeforeBarrier_Runs() =>
        ResumeLocalScenarios.ZeroLengthPointerBeforeBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_StackAllocPointerWithElements_KeepsItsValues() =>
        ResumeLocalScenarios.PointerElements(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_StackAllocPointerWithoutElements_StartsZeroed() =>
        ResumeLocalScenarios.PointerZeroed(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ZeroLengthStackAllocSpan_Runs() =>
        ResumeLocalScenarios.ZeroLengthSpan(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ZeroLengthStackAllocSpanBeforeBarrier_Runs() =>
        ResumeLocalScenarios.ZeroLengthSpanBeforeBarrier(s_runManaged);

    private static void RunManaged(Action launch)
    {
        bool previousState = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = true;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previousState;
        }
    }
}
#endif
