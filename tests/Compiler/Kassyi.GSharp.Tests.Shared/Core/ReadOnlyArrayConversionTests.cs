using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A <c>[GpuReadOnly]</c> array has const elements on the device, while C# converts it to every array and span type of
/// its element: each conversion builds and reads the array's elements on every path (#1709, #1071).
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class ReadOnlyArrayConversionTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedLocal_ReadOnlyField_ReadsTheField(bool forceManaged)
    {
        uint[] o = [0u];

        Run(forceManaged, () => ReadOnlyArrayConversionKernels.LaunchTypedLocalKernel(Holder(), o));

        Assert.Equal([13u], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void NotNullLocal_ReadOnlyField_ReadsTheField(bool forceManaged)
    {
        uint[] o = [0u];

        Run(forceManaged, () => ReadOnlyArrayConversionKernels.LaunchNotNullLocalKernel(Holder(), o));

        Assert.Equal([200u], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ReassignedLocal_ReadOnlyField_ReadsTheField(bool forceManaged)
    {
        uint[] o = [0u];

        Run(forceManaged, () => ReadOnlyArrayConversionKernels.LaunchReassignedLocalKernel(Holder(), o));

        Assert.Equal([20u], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceFunctionArgument_ReadOnlyFieldAndParameter_ReadsBoth(bool forceManaged)
    {
        uint[] o = [0u];
        uint[] r = [1000u, 2000u];

        Run(forceManaged, () => ReadOnlyArrayConversionKernels.LaunchDeviceFunctionArgumentKernel(Holder(), r, o));

        Assert.Equal([1070u], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldToSpan_ReadOnlyField_ReadsTheField(bool forceManaged)
    {
        uint[] o = [0u];

        Run(forceManaged, () => ReadOnlyArrayConversionKernels.LaunchFieldToSpanKernel(Holder(), o));

        Assert.Equal([30u], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void ParameterToReadOnlySpan_ImplicitAndCast_ReadTheParameter(bool forceManaged)
    {
        uint[] o = [0u];
        uint[] r = [4u, 5u, 6u];

        Run(forceManaged, () => ReadOnlyArrayConversionKernels.LaunchParameterToReadOnlySpanKernel(r, o));

        Assert.Equal([12u], o);
    }

    private static ReadOnlyArrayHolder Holder() => new() { Fixed = [10u, 20u, 30u], Optional = [100u, 200u] };

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
