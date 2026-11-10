#if GSHARP_CPU
using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The scenarios of <see cref="PrimaryConstructorTests"/> on the managed fallback, which runs the C# (#495).</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class PrimaryConstructorManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = ManagedKernelRun.Run;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrimaryConstructor_ManagedExecution_RunsInitializers() =>
        PrimaryConstructorScenarios.InitializersRun(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PrimaryConstructor_ManagedWithObjectInitializer_RunsBeforeObjectInitializer() =>
        PrimaryConstructorScenarios.InitializersRunBeforeObjectInitializer(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void CapturedParameters_ManagedStructDeclaration_StoredInStruct() =>
        PrimaryConstructorScenarios.CapturedParametersAreStored(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CapturedParameters_ManagedDeviceInvocation_CrossesToKernelAndBack() =>
        PrimaryConstructorScenarios.CapturedParametersCrossToTheDevice(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CapturedParameters_ManagedHostLayout_MatchesHostLayout() =>
        PrimaryConstructorScenarios.CapturedParametersMatchTheHostLayout(s_runManaged, checkShadow: false);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void RecordStruct_ManagedExecution_InitializesPropertiesThenMembers() =>
        PrimaryConstructorScenarios.RecordStructInitializesItsProperties(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ChainedConstructor_ManagedExecution_RunsPrimaryOneFirst() =>
        PrimaryConstructorScenarios.ChainedConstructorRunsThePrimaryOneFirst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ParameterlessPrimaryConstructor_ManagedExecution_RunsInitializers() =>
        PrimaryConstructorScenarios.ParameterlessPrimaryConstructorRunsInitializers(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GenericNew_ManagedExecution_RunsParameterlessConstructor() =>
        PrimaryConstructorScenarios.GenericNewRunsTheParameterlessConstructor(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void GenericNew_ManagedInitializerChain_KeepsInitializerChainAndDefaultOrder() =>
        PrimaryConstructorScenarios.GenericNewKeepsConstructorOrder(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CapturedParametersAndAutoProperties_ManagedRoundtrip_MatchesExpectedValues() =>
        PrimaryConstructorScenarios.CapturedParametersAndAutoPropertiesRoundtrip(s_runManaged, checkShadow: false);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void PartialStruct_ManagedExecution_RunsPrimaryConstructor() =>
        PrimaryConstructorScenarios.PartialStructRunsItsPrimaryConstructor(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void PartialStruct_ManagedHostLayout_MatchesHostLayout() =>
        PrimaryConstructorScenarios.PartialStructMatchesTheHostLayout(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedCapturedParameter_ManagedRoundtrip_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterRoundtrips(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void NestedConvertedCapturedParameter_ManagedRoundtrip_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.NestedConvertedCapturedParameterRoundtrips(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedCapturedParameterArray_ManagedRoundtrip_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterArrayRoundtrips(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedCapturedParameterInInlineArray_ManagedRoundtrip_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterInInlineArrayRoundtrips(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SizedConvertedCapturedParameter_ManagedRoundtrip_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.SizedConvertedCapturedParameterRoundtrips(s_runManaged);
}
#endif
