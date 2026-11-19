using System;
using System.Linq;
using System.Reflection;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A local const whose declaration a goto jumps over, read after a barrier, on the state machine of the native and the
/// managed CPU paths.
/// </summary>
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class LocalConstSkippedByGotoTests : TranspilerTestBase
{
    private const int ThreadCount = 2;

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ConstSkippedByGoto_NativeStateMachine_IsReadOnEveryPath()
    {
        Skip.If(IsStackBuild(), "The one-stack C++ splits the jumped-over const declaration (a separate issue)");
        var result = new int[ThreadCount];

        LocalConstSkippedByGotoKernels.LaunchConstSkippedByGotoKernel(result, blockDimX: ThreadCount);

        Assert.Equal([3, 4], result);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.Boundary)]
    public void ConstSkippedByGoto_ManagedStateMachine_IsReadOnEveryPath()
    {
        var result = new int[ThreadCount];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            LocalConstSkippedByGotoKernels.LaunchConstSkippedByGotoKernel(result, blockDimX: ThreadCount);
        }

        Assert.Equal([3, 4], result);
    }

    // A build with GSharpNativeBarrierModel=Stack runs this kernel on stacks, not on the state machine
    private static bool IsStackBuild() =>
        typeof(LocalConstSkippedByGotoTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Any(static a => string.Equals(a.Key, "GSharpNativeBarrierModel", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(a.Value, "Stack", StringComparison.OrdinalIgnoreCase));
}
