using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Structs with a primary constructor on the native dialects (#495): <c>new S(args)</c> runs the constructor as a
/// factory function, and the parameters that members read are stored in device members laid out as on the host.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class PrimaryConstructorTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = static launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void PrimaryConstructor_Invocation_RunsInitializers() =>
        PrimaryConstructorScenarios.InitializersRun(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void PrimaryConstructor_WithObjectInitializer_RunsBeforeObjectInitializer() =>
        PrimaryConstructorScenarios.InitializersRunBeforeObjectInitializer(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void CapturedParameters_StructDeclaration_StoredInStruct() =>
        PrimaryConstructorScenarios.CapturedParametersAreStored(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CapturedParameters_DeviceInvocation_CrossesToDeviceAndBack() =>
        PrimaryConstructorScenarios.CapturedParametersCrossToTheDevice(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CapturedParameters_HostLayout_MatchesHostLayout() =>
        PrimaryConstructorScenarios.CapturedParametersMatchTheHostLayout(s_runNative, checkShadow: true);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void RecordStruct_Invocation_InitializesPropertiesThenMembers() =>
        PrimaryConstructorScenarios.RecordStructInitializesItsProperties(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ChainedConstructor_Invocation_RunsPrimaryOneFirst() =>
        PrimaryConstructorScenarios.ChainedConstructorRunsThePrimaryOneFirst(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ParameterlessPrimaryConstructor_Invocation_RunsInitializers() =>
        PrimaryConstructorScenarios.ParameterlessPrimaryConstructorRunsInitializers(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void GenericNew_Invocation_RunsParameterlessConstructor() =>
        PrimaryConstructorScenarios.GenericNewRunsTheParameterlessConstructor(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void GenericNew_InitializerChain_KeepsInitializerChainAndDefaultOrder() =>
        PrimaryConstructorScenarios.GenericNewKeepsConstructorOrder(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void CapturedParametersAndAutoProperties_Roundtrip_MatchesExpectedValues() =>
        PrimaryConstructorScenarios.CapturedParametersAndAutoPropertiesRoundtrip(s_runNative, checkShadow: true);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void PartialStruct_Invocation_RunsPrimaryConstructor() =>
        PrimaryConstructorScenarios.PartialStructRunsItsPrimaryConstructor(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void PartialStruct_HostLayout_MatchesHostLayout() =>
        PrimaryConstructorScenarios.PartialStructMatchesTheHostLayout(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedCapturedParameter_Invocation_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterRoundtrips(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void NestedConvertedCapturedParameter_Invocation_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.NestedConvertedCapturedParameterRoundtrips(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedCapturedParameterArray_Invocation_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterArrayRoundtrips(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ConvertedCapturedParameterInInlineArray_Invocation_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterInInlineArrayRoundtrips(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void SizedConvertedCapturedParameter_Invocation_RoundtripsSuccessfully() =>
        PrimaryConstructorScenarios.SizedConvertedCapturedParameterRoundtrips(s_runNative);
}
