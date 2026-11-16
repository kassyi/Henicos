#nullable enable
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Early null checks at device function calls explain why they differ from C# (#2044):
/// G# checks the nullable annotation at the call boundary, earlier than C# (which throws only when read).
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class DeviceFunctionEarlyNullCheckTests : TranspilerTestBase
{
    private void RunNative(Action launch)
    {
        Skip.If(IsCudaDialect,
            "A CUDA trap leaves the context of the test process unusable; " +
            "differential null cases run in child processes (#1409).");

        using (GpuExecution.Use(KernelExecution.Default))
        {
            launch();
        }
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void EarlyNullCheck_DirectCallWithNullArray_ThrowsWithExplanation()
    {
        var output = new int[1];
        var exception = Assert.Throws<NullReferenceException>(() =>
            RunNative(() => DeviceFunctionEarlyNullCheckKernels.LaunchDirectKernel(null, output)));

        Assert.Contains("non-nullable parameter 'arr'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ProbeDirect", exception.Message, StringComparison.Ordinal);
        Assert.Contains("earlier than C#", exception.Message, StringComparison.Ordinal);
        Assert.Contains("int[]?", exception.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void EarlyNullCheck_NestedCallInDeviceFunctionWithNullArray_ThrowsWithExplanation()
    {
        var output = new int[1];
        var exception = Assert.Throws<NullReferenceException>(() =>
            RunNative(() => DeviceFunctionEarlyNullCheckKernels.LaunchNestedCallerKernel(null, output)));

        Assert.Contains("non-nullable parameter 'x'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("LeafCallee", exception.Message, StringComparison.Ordinal);
        Assert.Contains("earlier than C#", exception.Message, StringComparison.Ordinal);
        Assert.Contains("int[]?", exception.Message, StringComparison.Ordinal);
        Assert.Contains(" at ", exception.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void EarlyNullCheck_StructArgumentWithNullNestedField_ThrowsWithFieldPath()
    {
        var output = new int[1];
        var bag = new DeviceFunctionOuterBag
        {
            Inner = new DeviceFunctionInnerBag { Data = null! },
            Other = [1, 2]
        };

        var exception = Assert.Throws<NullReferenceException>(() =>
            RunNative(() => DeviceFunctionEarlyNullCheckKernels.LaunchStructFieldKernel(bag, output)));

        Assert.Contains("field 's.Inner.Data' of parameter 's'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ProbeStruct", exception.Message, StringComparison.Ordinal);
        Assert.Contains("earlier than C#", exception.Message, StringComparison.Ordinal);
        Assert.Contains("int[]?", exception.Message, StringComparison.Ordinal);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void EarlyNullCheck_StructArgumentWithNullSecondField_ThrowsWithSecondFieldPath()
    {
        var output = new int[1];
        var bag = new DeviceFunctionOuterBag
        {
            Inner = new DeviceFunctionInnerBag { Data = [1] },
            Other = null!
        };

        var exception = Assert.Throws<NullReferenceException>(() =>
            RunNative(() => DeviceFunctionEarlyNullCheckKernels.LaunchStructFieldKernel(bag, output)));

        Assert.Contains("field 's.Other' of parameter 's'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ProbeStruct", exception.Message, StringComparison.Ordinal);
        Assert.Contains("earlier than C#", exception.Message, StringComparison.Ordinal);
        Assert.Contains("int[]?", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Inner.Data", exception.Message, StringComparison.Ordinal);
    }
}
