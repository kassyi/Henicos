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
    public void Test_PrimaryConstructor_RunsInitializers() =>
        PrimaryConstructorScenarios.InitializersRun(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PrimaryConstructor_RunsBeforeObjectInitializer() =>
        PrimaryConstructorScenarios.InitializersRunBeforeObjectInitializer(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_CapturedParameters_AreStoredInTheStruct() =>
        PrimaryConstructorScenarios.CapturedParametersAreStored(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CapturedParameters_CrossToTheDeviceAndBack() =>
        PrimaryConstructorScenarios.CapturedParametersCrossToTheDevice(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CapturedParameters_MatchTheHostLayout() =>
        PrimaryConstructorScenarios.CapturedParametersMatchTheHostLayout(s_runNative, checkShadow: true);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RecordStruct_InitializesPropertiesThenMembers() =>
        PrimaryConstructorScenarios.RecordStructInitializesItsProperties(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_ChainedConstructor_RunsThePrimaryOneFirst() =>
        PrimaryConstructorScenarios.ChainedConstructorRunsThePrimaryOneFirst(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ParameterlessPrimaryConstructor_RunsInitializers() =>
        PrimaryConstructorScenarios.ParameterlessPrimaryConstructorRunsInitializers(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GenericNew_RunsTheParameterlessConstructor() =>
        PrimaryConstructorScenarios.GenericNewRunsTheParameterlessConstructor(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_CapturedParametersAndAutoProperties_Roundtrip() =>
        PrimaryConstructorScenarios.CapturedParametersAndAutoPropertiesRoundtrip(s_runNative, checkShadow: true);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_PartialStruct_RunsItsPrimaryConstructor() =>
        PrimaryConstructorScenarios.PartialStructRunsItsPrimaryConstructor(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_PartialStruct_MatchesTheHostLayout() =>
        PrimaryConstructorScenarios.PartialStructMatchesTheHostLayout(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ConvertedCapturedParameter_Roundtrips() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterRoundtrips(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_NestedConvertedCapturedParameter_Roundtrips() =>
        PrimaryConstructorScenarios.NestedConvertedCapturedParameterRoundtrips(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ConvertedCapturedParameterArray_Roundtrips() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterArrayRoundtrips(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_ConvertedCapturedParameterInInlineArray_Roundtrips() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterInInlineArrayRoundtrips(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_SizedConvertedCapturedParameter_Roundtrips() =>
        PrimaryConstructorScenarios.SizedConvertedCapturedParameterRoundtrips(s_runNative);
}
