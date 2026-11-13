#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback of <see cref="GotoScopeTests"/>.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class GotoScopeManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 2)]
    public void Goto_ManagedOverNestedBlock_JumpsOverTheBlock(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoOverNestedBlockKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 5)]
    public void Goto_ManagedOverTwoNestedBlocks_JumpsOverTheBlocks(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoOverDoublyNestedBlockKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 6)]
    public void Goto_ManagedOverLabeledBlock_JumpsOverTheBlock(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoOverLabeledBlockKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 3)]
    public void Goto_ManagedOutOfNestedBlock_SkipsTheRestOfTheBlock(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoOutOfNestedBlockKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 6)]
    public void Goto_ManagedForwardToAnotherSection_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoForwardAcrossSectionsKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 3)]
    public void Goto_ManagedBackwardToAnotherSection_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoBackwardAcrossSectionsKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 3)]
    public void Goto_ManagedPastLabeledDeclaration_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoPastLabeledDeclarationKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 22)]
    public void Goto_ManagedOverNestedArrayFieldStruct_JumpsOverTheBlock(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoOverNestedStructKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 22)]
    public void Goto_ManagedAcrossSectionsPastArrayFieldStruct_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoAcrossSectionsPastStructKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 23)]
    public void Goto_ManagedPastLabeledArrayFieldStruct_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runManaged,
            static (data, f) => GotoScopeKernels.LaunchGotoPastLabeledStructKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
#endif
