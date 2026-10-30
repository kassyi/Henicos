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
    public void Test_Managed_PrimaryConstructor_RunsInitializers() =>
        PrimaryConstructorScenarios.InitializersRun(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_PrimaryConstructor_RunsBeforeObjectInitializer() =>
        PrimaryConstructorScenarios.InitializersRunBeforeObjectInitializer(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_CapturedParameters_AreStoredInTheStruct() =>
        PrimaryConstructorScenarios.CapturedParametersAreStored(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_CapturedParameters_CrossToTheKernelAndBack() =>
        PrimaryConstructorScenarios.CapturedParametersCrossToTheDevice(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_CapturedParameters_MatchTheHostLayout() =>
        PrimaryConstructorScenarios.CapturedParametersMatchTheHostLayout(s_runManaged, checkShadow: false);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_RecordStruct_InitializesPropertiesThenMembers() =>
        PrimaryConstructorScenarios.RecordStructInitializesItsProperties(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_ChainedConstructor_RunsThePrimaryOneFirst() =>
        PrimaryConstructorScenarios.ChainedConstructorRunsThePrimaryOneFirst(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ParameterlessPrimaryConstructor_RunsInitializers() =>
        PrimaryConstructorScenarios.ParameterlessPrimaryConstructorRunsInitializers(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_GenericNew_RunsTheParameterlessConstructor() =>
        PrimaryConstructorScenarios.GenericNewRunsTheParameterlessConstructor(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_GenericNew_KeepsInitializerChainAndDefaultOrder() =>
        PrimaryConstructorScenarios.GenericNewKeepsConstructorOrder(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_CapturedParametersAndAutoProperties_Roundtrip() =>
        PrimaryConstructorScenarios.CapturedParametersAndAutoPropertiesRoundtrip(s_runManaged, checkShadow: false);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_PartialStruct_RunsItsPrimaryConstructor() =>
        PrimaryConstructorScenarios.PartialStructRunsItsPrimaryConstructor(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_PartialStruct_MatchesTheHostLayout() =>
        PrimaryConstructorScenarios.PartialStructMatchesTheHostLayout(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ConvertedCapturedParameter_Roundtrips() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterRoundtrips(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_NestedConvertedCapturedParameter_Roundtrips() =>
        PrimaryConstructorScenarios.NestedConvertedCapturedParameterRoundtrips(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ConvertedCapturedParameterArray_Roundtrips() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterArrayRoundtrips(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_ConvertedCapturedParameterInInlineArray_Roundtrips() =>
        PrimaryConstructorScenarios.ConvertedCapturedParameterInInlineArrayRoundtrips(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_SizedConvertedCapturedParameter_Roundtrips() =>
        PrimaryConstructorScenarios.SizedConvertedCapturedParameterRoundtrips(s_runManaged);
}
#endif
