using System;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Several shared memory declarations in one block, on the native dialects. CUDA is the reference: the managed
/// fallback must produce the same results.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
public class SharedMemoryRegionTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runNative = launch => launch();

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_LaterDeclaration_KeepsEarlierBuffer()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        SharedMemoryRegionScenarios.LaterDeclarationKeepsEarlierBuffer(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_MixedElementTypes_KeepTheirOwnValues()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        SharedMemoryRegionScenarios.MixedElementTypes(s_runNative);
    }

    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Test_GrowingDeclaration_KeepsLargerCount()
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        SharedMemoryRegionScenarios.GrowingDeclaration(s_runNative);
    }
}
