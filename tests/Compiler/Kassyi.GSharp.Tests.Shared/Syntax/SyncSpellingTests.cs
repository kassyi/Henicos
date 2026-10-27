using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A barrier or warp shuffle spelled through a <c>using</c> alias or <c>using static</c> splits the state machine on the
/// native CPU dialect and runs as the intrinsic on CUDA (#503).
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class SyncSpellingTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AliasBarrier_OrdersSharedMemoryAccesses()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        SyncSpellingScenarios.AliasBarrier(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticBarrier_OrdersSharedMemoryAccesses()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        SyncSpellingScenarios.UsingStaticBarrier(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AliasShuffleDown_ReadsTheNextLane() =>
        SyncSpellingScenarios.AliasShuffle(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticShuffleDown_ReadsTheNextLane() =>
        SyncSpellingScenarios.UsingStaticShuffle(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticAtomicAdd_MapsToTheNativeIntrinsic()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        SyncSpellingScenarios.UsingStaticAtomic(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_QualifiedBarrier_OrdersSharedMemoryAccesses()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        SyncSpellingScenarios.QualifiedBarrier(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_UserMethodNamedSyncThreads_IsCalled() =>
        SyncSpellingScenarios.UserSyncThreads(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_LookalikeGpuSyncThreads_IsCalled() =>
        SyncSpellingScenarios.LookalikeGpuSyncThreads(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticThreadIndex_MapsByTheTypePattern() =>
        SyncSpellingScenarios.UsingStaticThreadIndex(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_UsingStaticFastExp_MapsByTheDialectSignature() =>
        SyncSpellingScenarios.UsingStaticFastExp(s_runNative);

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_OwnPopCount_IsNotTheIntrinsic() =>
        SyncSpellingScenarios.OwnPopCount(s_runNative);
}
