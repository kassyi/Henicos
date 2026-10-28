#if GSHARP_CPU
using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The <c>Gpu.Atomic*</c> family on the managed C# fallback gives the values of the native dialects (#728): the same
/// final values, and old values that one order of the operations explains (<see cref="AtomicIntrinsicsScenarios"/>).
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class AtomicIntrinsicsManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = ManagedKernelRun.Run;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AtomicArithmetic_AllThreadsContributeToSingleAddress() =>
        AtomicIntrinsicsScenarios.Arithmetic(s_runManaged);

    [Theory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u, 0u)]
    [InlineData(0u, 9u)]
    [InlineData(0u, uint.MaxValue)]
    [InlineData(100u, 9u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void Test_Managed_AtomicIncDec_WrapsAroundLimit(uint initial, uint limit) =>
        AtomicIntrinsicsScenarios.IncDecWrap(s_runManaged, initial, limit);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AtomicFloatAdd_ManyBlocks() => AtomicIntrinsicsScenarios.FloatAddHighContention(s_runManaged);

    // The atomic is in a device function that does not suspend, which the managed path calls as the user's C# method
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_AtomicThroughRefParameter_UpdatesCallerElement() =>
        AtomicIntrinsicsScenarios.ThroughRefParameter(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_EveryAtomicOverload_ReturnsTheOldValueOfOneOrder() =>
        AtomicIntrinsicsScenarios.OldValues(s_runManaged);
}
#endif
