#if GSHARP_CPU
using System;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>The managed C# fallback runs the same lowered state machine, so a spelled barrier or shuffle must suspend there too (#503).</summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SyncSpellingManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyncSpelling_ManagedAliasBarrier_OrdersSharedMemoryAccesses() =>
        SyncSpellingScenarios.AliasBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyncSpelling_ManagedUsingStaticBarrier_OrdersSharedMemoryAccesses() =>
        SyncSpellingScenarios.UsingStaticBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyncSpelling_ManagedAliasShuffleDown_ReadsTheNextLane() =>
        SyncSpellingScenarios.AliasShuffle(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyncSpelling_ManagedUsingStaticShuffleDown_ReadsTheNextLane() =>
        SyncSpellingScenarios.UsingStaticShuffle(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyncSpelling_ManagedQualifiedBarrier_OrdersSharedMemoryAccesses() =>
        SyncSpellingScenarios.QualifiedBarrier(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SyncSpelling_ManagedUserMethodNamedSyncThreads_IsCalled() =>
        SyncSpellingScenarios.UserSyncThreads(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SyncSpelling_ManagedLookalikeGpuSyncThreads_IsCalled() =>
        SyncSpellingScenarios.LookalikeGpuSyncThreads(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyncSpelling_ManagedUsingStaticThreadIndex_MapsByTheTypePattern() =>
        SyncSpellingScenarios.UsingStaticThreadIndex(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void SyncSpelling_ManagedUsingStaticFastExp_MapsByTheDialectSignature() =>
        SyncSpellingScenarios.UsingStaticFastExp(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SyncSpelling_ManagedOwnPopCount_IsNotTheIntrinsic() =>
        SyncSpellingScenarios.OwnPopCount(s_runManaged);

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
