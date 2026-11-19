using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Local <c>const</c> values read after a barrier or a warp shuffle, on the native CPU path (Tests.Cpu) and on CUDA
/// (Tests.Cuda).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class LocalConstAcrossBarrierTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runDefault = static launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LocalConst_AcrossBarrier_KeepsConstantConversion() =>
        LocalConstAcrossBarrierScenarios.ConstAcrossBarrier(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_InLoopShuffle_KeepsConstantArgument() =>
        LocalConstAcrossBarrierScenarios.LocalConstInLoop(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_UintMinusConstInt_SubtractsAsUint() =>
        LocalConstAcrossBarrierScenarios.ConstConversionChangesOperator(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_TypedVariationsAcrossBarrier_PreservesTypesAndValues() =>
        LocalConstAcrossBarrierScenarios.TypedLocalConstsAcrossBarrier(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ZeroFloatDivisorsAcrossBarrier_GiveInfinitiesAndNaN() =>
        LocalConstAcrossBarrierScenarios.ZeroFloatConstDivisorsAcrossBarrier(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_SpecialFloatsAcrossBarrier_KeepTheirBits() =>
        LocalConstAcrossBarrierScenarios.SpecialFloatConstsAcrossBarrier(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_SiblingScopesOfOneName_KeepTheirOwnValues() =>
        LocalConstAcrossBarrierScenarios.SiblingScopeConsts(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_InitializerReadsConstAfterBarrier_StaysConstant() =>
        LocalConstAcrossBarrierScenarios.ConstInitializerAfterBarrier(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ReadByMovedLocalFunction_StaysConstant() =>
        LocalConstAcrossBarrierScenarios.MovedLocalFunctionConst(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_NameofOfRenamedConst_KeepsSourceName() =>
        LocalConstAcrossBarrierScenarios.NameofConstAcrossBarrier(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_ByteEnumAcrossBarrier_KeepsValue() =>
        LocalConstAcrossBarrierScenarios.EnumConstAcrossBarrier(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_TwoDeclaratorsAcrossBarrier_StayConstant() =>
        LocalConstAcrossBarrierScenarios.MultiDeclaratorConst(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_LabeledDeclarationAcrossBarrier_StaysConstant() =>
        LocalConstAcrossBarrierScenarios.LabeledConst(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_DeclaredInLocalFunction_StaysInTheFunction() =>
        LocalConstAcrossBarrierScenarios.ConstInsideLocalFunction(s_runDefault);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void LocalConst_SuspendingDeviceFunction_KeepsConstantConversion() =>
        LocalConstAcrossBarrierScenarios.DeviceFunctionLocalConst(s_runDefault);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void LocalConst_SwitchScopedBlockConstsAcrossSections_KeepsPerSectionValue(int flag) =>
        LocalConstAcrossBarrierScenarios.SwitchScopedBlockConstsAcrossSections(s_runDefault, flag);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0)]
    [InlineData(1)]
    public void LocalConst_SwitchSameSectionTwoBlocksConsts_KeepsPerBlockValue(int flag) =>
        LocalConstAcrossBarrierScenarios.SwitchSameSectionTwoBlocksConsts(s_runDefault, flag);
}
