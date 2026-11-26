using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A goto that jumps past an initialized declaration outside its own statement list (a nested block, another switch
/// section, a label) on the native dialects. The CPU module is built by g++ on Linux, which rejects a jump into the C++
/// scope of an initialization.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class GotoScopeTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 2)]
    public void Goto_NativeOverNestedBlock_JumpsOverTheBlock(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoOverNestedBlockKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 5)]
    public void Goto_NativeOverTwoNestedBlocks_JumpsOverTheBlocks(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoOverDoublyNestedBlockKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 6)]
    public void Goto_NativeOverLabeledBlock_JumpsOverTheBlock(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoOverLabeledBlockKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 3)]
    public void Goto_NativeOutOfNestedBlock_SkipsTheRestOfTheBlock(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoOutOfNestedBlockKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 6)]
    public void Goto_NativeForwardToAnotherSection_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoForwardAcrossSectionsKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 3)]
    public void Goto_NativeBackwardToAnotherSection_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoBackwardAcrossSectionsKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 3)]
    public void Goto_NativePastLabeledDeclaration_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoPastLabeledDeclarationKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 22)]
    public void Goto_NativeOverNestedArrayFieldStruct_JumpsOverTheBlock(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoOverNestedStructKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 22)]
    public void Goto_NativeAcrossSectionsPastArrayFieldStruct_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoAcrossSectionsPastStructKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 23)]
    public void Goto_NativePastLabeledArrayFieldStruct_SkipsTheDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoPastLabeledStructKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(5, 1)]
    public void Goto_NativeIntoSectionBeforeStruct_DeclaresTheStructBeforeTheSwitch(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoIntoSectionBeforeStructKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 2)]
    public void Goto_NativeOverFixed_SplitsThePointerDeclaration(int flag, int expectedFirst) =>
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoOverFixedKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 4)]
    public void Goto_NativeOverSharedMemory_SplitsTheAllocation(int flag, int expectedFirst)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoOverSharedMemoryKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 4)]
    public void Goto_NativeOverSharedMemoryAlias_SplitsThePointerDeclaration(int flag, int expectedFirst)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        GotoScopeScenarios.Run(
            s_runNative,
            static (data, f) => GotoScopeKernels.LaunchGotoOverSharedMemoryAliasKernel(data, f, blockDimX: 1),
            flag,
            expectedFirst
        );
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Goto_PastNullableLocal_SkipsTheDeclaration()
    {
        var result = new uint[8];

        GotoScopeKernels.LaunchGotoPastNullableKernel(result, blockDimX: 8);

        // The even lanes jump past the local; the odd lanes 1 and 3 read 9, lanes 5 and 7 their index
        Assert.Equal([1u, 10u, 1u, 10u, 1u, 6u, 1u, 8u], result);
    }
}
