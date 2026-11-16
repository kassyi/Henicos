using System;
using Kassyi.GSharp.Runtime.Aot;
using Xunit;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// <c>Gpu.NotNull</c> returns its array, and throws <see cref="NullReferenceException"/> at the call when the array is
/// null: on the native CPU build with the place of the call, on the managed path from the C# method. A null kernel
/// argument is null there too, as in a C# call, while an empty one is not.
/// </summary>
/// <remarks>
/// The null cases are skipped on CUDA, as in <see cref="NullArrayFieldTests"/>; the differential case
/// <c>NullValidation_GpuNotNull_ThrowsAtTheCall</c> runs them on CUDA with the place of the call.
/// </remarks>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public partial class GpuNotNullTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Sum_NonNullField_ReadsTheArrayThroughTheLocal(bool forceManaged)
    {
        var o = new[] { -1, -1 };

        var holder = new NotNullHolder { Data = [1, 2, 3], Offset = 10 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchSumKernel(holder, o));

        Assert.Equal([16, 1], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void DeviceFunction_NonNullField_ReturnsTheFirstElement(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchDeviceFunctionKernel(new NotNullHolder { Data = [7, 8] }, o));

        Assert.Equal([7], o);
    }

    // An empty array is not null
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Length_EmptyField_IsNotNull(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchLengthKernel(new NotNullHolder { Data = [] }, o));

        Assert.Equal([0], o);
    }

    // An empty kernel argument is passed as one on every path, so Gpu.NotNull returns it
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Optional_EmptyArgument_IsNotNullAndReadsLengthZero(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchOptionalKernel(o, []));

        Assert.Equal([0], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Optional_NonNullArgument_ReadsItsLength(bool forceManaged)
    {
        var o = new[] { -1 };

        Run(forceManaged, () => GpuNotNullKernels.LaunchOptionalKernel(o, [1, 2, 3]));

        Assert.Equal([3], o);
    }

    // The managed execution path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
