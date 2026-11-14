using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Ref locals on the native dialects: the C++ holds each one as a pointer.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class RefLocalTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void AliasOfLocal_Native_ChangesTheLocal() =>
        RefLocalScenarios.AliasOfLocal(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void AliasOfElement_Native_ChangesTheElement() =>
        RefLocalScenarios.AliasOfElement(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Rebind_Native_BindsTheLocalToAnotherVariable() =>
        RefLocalScenarios.Rebind(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1, new[] { 9, 2 })]
    [InlineData(0, new[] { 1, 9 })]
    public void Conditional_Native_BindsTheChosenVariable(int c, int[] expected) =>
        RefLocalScenarios.Conditional(s_runNative, c, expected);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void RefReadOnly_Native_SeesTheValueWrittenThroughAnotherPath() =>
        RefLocalScenarios.RefReadOnly(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void ForEachRef_NativeSpan_ChangesTheElementOfEachThread() =>
        RefLocalScenarios.ForEachRefSpan(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void ForEachRef_NativeArrayWithContinueAndBreak_ChangesTheVisitedElements() =>
        RefLocalScenarios.ForEachRefArray(s_runNative);

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0, 10)]
    [InlineData(1, 5)]
    public void GotoPastRefLocal_Native_SkipsTheDeclaration(int flag, int expectedFirst) =>
        RefLocalScenarios.GotoPastRefLocal(s_runNative, flag, expectedFirst);
}
