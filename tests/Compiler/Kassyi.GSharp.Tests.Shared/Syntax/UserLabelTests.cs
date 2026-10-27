using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>Labels of the user on the native dialects. CUDA is the reference: the CPU dialect must give the same results.</summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class UserLabelTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LabeledShuffle_RunsAgainOnEveryGoto() => UserLabelScenarios.LabeledShuffleLoop(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LabeledBarrier_SeparatesWritesFromReads()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        UserLabelScenarios.LabeledBarrier(s_runNative);
    }
}
