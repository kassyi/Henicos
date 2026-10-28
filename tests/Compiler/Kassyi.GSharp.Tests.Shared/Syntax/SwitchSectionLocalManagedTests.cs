#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback must give the same results as the native dialects for shared switch locals.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SwitchSectionLocalManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_InitializedInFirstSection_AssignedInLaterSection(int flag) =>
        SwitchSectionLocalScenarios.InitializedInFirstSection(s_runManaged, flag);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SharedStructAndVar_EverySectionSeesTheLocals() =>
        SwitchSectionLocalScenarios.SharedStructAndVar(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_GotoCaseAndNestedSwitch_LocalCarriedIntoNextSection() =>
        SwitchSectionLocalScenarios.GotoCaseAndNestedSwitch(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void Test_Managed_SectionConstant_LaterSectionReadsTheValue(int flag) =>
        SwitchSectionLocalScenarios.SectionConstant(s_runManaged, flag);

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
