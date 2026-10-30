using System;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// The <c>Gpu.Atomic*</c> family on the native dialects: the CUDA build and the native CPU build
/// (<see cref="AtomicIntrinsicsScenarios"/>).
/// </summary>
public class AtomicIntrinsicsTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = static launch => launch();

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AtomicArithmetic_AllThreadsContributeToSingleAddress()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        AtomicIntrinsicsScenarios.Arithmetic(s_runNative);
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(0u, 0u)]
    [InlineData(0u, 1u)]
    [InlineData(0u, 9u)]
    [InlineData(0u, 31u)]
    [InlineData(0u, 32u)]
    [InlineData(0u, uint.MaxValue)]
    [InlineData(100u, 9u)]
    [InlineData(uint.MaxValue, uint.MaxValue)]
    public void Test_AtomicIncDec_WrapsAroundLimit(uint initial, uint limit)
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        AtomicIntrinsicsScenarios.IncDecWrap(s_runNative, initial, limit);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AtomicFloatAdd_HighContention()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        AtomicIntrinsicsScenarios.FloatAddHighContention(s_runNative);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_AtomicThroughRefParameter_UpdatesCallerElement()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        AtomicIntrinsicsScenarios.ThroughRefParameter(s_runNative);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Syntax)]
    [Scenario(TestScenario.Boundary)]
    public void Test_EveryAtomicOverload_ReturnsTheOldValueOfOneOrder()
    {
        EnsureCapability(DialectCapabilities.HardwareAtomics);
        AtomicIntrinsicsScenarios.OldValues(s_runNative);
    }
}
