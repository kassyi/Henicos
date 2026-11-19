using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Local <c>const</c> values read after a barrier or a warp shuffle, on the managed CPU path.</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class LocalConstAcrossBarrierManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void LocalConst_ManagedAcrossBarrier_KeepsConstantConversion() =>
        LocalConstAcrossBarrierScenarios.ConstAcrossBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedInLoopShuffle_KeepsConstantArgument() =>
        LocalConstAcrossBarrierScenarios.LocalConstInLoop(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedUintMinusConstInt_SubtractsAsUint() =>
        LocalConstAcrossBarrierScenarios.ConstConversionChangesOperator(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedShortMaxIncremented_WrapsToMinValue() =>
        LocalConstAcrossBarrierScenarios.ShortConstWrap(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedShortConstsAdded_PromoteToInt() =>
        LocalConstAcrossBarrierScenarios.ShortConstSum(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedIntConstToUshort_ConvertsAsConstant() =>
        LocalConstAcrossBarrierScenarios.UshortConstConversion(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedCharConstToInt_KeepsCode() =>
        LocalConstAcrossBarrierScenarios.CharConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedBoolConsts_CombineAsConstants() =>
        LocalConstAcrossBarrierScenarios.BoolConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedByteConstInMathMax_PicksIntOverload() =>
        LocalConstAcrossBarrierScenarios.ByteConstMax(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedNaNConst_IsNotEqualToItself() =>
        LocalConstAcrossBarrierScenarios.NanConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedUlongConstMask_KeepsHighBit() =>
        LocalConstAcrossBarrierScenarios.UlongConstMask(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedZeroFloatDivisorsAcrossBarrier_GiveInfinitiesAndNaN() =>
        LocalConstAcrossBarrierScenarios.ZeroFloatConstDivisorsAcrossBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedSpecialFloatsAcrossBarrier_KeepTheirBits() =>
        LocalConstAcrossBarrierScenarios.SpecialFloatConstsAcrossBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedSiblingScopesOfOneName_KeepTheirOwnValues() =>
        LocalConstAcrossBarrierScenarios.SiblingScopeConsts(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedInitializerReadsConstAfterBarrier_StaysConstant() =>
        LocalConstAcrossBarrierScenarios.ConstInitializerAfterBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedReadByMovedLocalFunction_StaysConstant() =>
        LocalConstAcrossBarrierScenarios.MovedLocalFunctionConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedReadByStaticLocalFunction_StaysConstant() =>
        LocalConstAcrossBarrierScenarios.StaticLocalFunctionConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedNameofOfRenamedConst_KeepsSourceName() =>
        LocalConstAcrossBarrierScenarios.NameofConstAcrossBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedByteEnumAcrossBarrier_KeepsValue() =>
        LocalConstAcrossBarrierScenarios.EnumConstAcrossBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedTwoDeclaratorsAcrossBarrier_StayConstant() =>
        LocalConstAcrossBarrierScenarios.MultiDeclaratorConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedLabeledDeclarationAcrossBarrier_StaysConstant() =>
        LocalConstAcrossBarrierScenarios.LabeledConst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedDeclaredInLocalFunction_StaysInTheFunction() =>
        LocalConstAcrossBarrierScenarios.ConstInsideLocalFunction(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ManagedSuspendingDeviceFunction_KeepsConstantConversion() =>
        LocalConstAcrossBarrierScenarios.DeviceFunctionLocalConst(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LocalConst_ManagedSwitchScopedBlockConstsAcrossSections_KeepsPerSectionValue(int flag) =>
        LocalConstAcrossBarrierScenarios.SwitchScopedBlockConstsAcrossSections(s_runManaged, flag);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void LocalConst_ManagedSwitchSameSectionTwoBlocksConsts_KeepsPerBlockValue(int flag) =>
        LocalConstAcrossBarrierScenarios.SwitchSameSectionTwoBlocksConsts(s_runManaged, flag);

    private static void RunManaged(Action launch)
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            launch();
        }
    }
}
