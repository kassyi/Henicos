using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// A file-local type in a body that the managed path does not copy runs on every dialect and path; in a body that it
/// has to copy, the managed launch throws and the other paths run (GSH0119).
/// </summary>
[Category(TestCategory.Syntax)]
[TestTarget(ExecutionTarget.GpuExecution)]
public class FileLocalTypeTests : TranspilerTestBase
{
    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchApplyKernel_FileLocalStructInDeviceFunction_ScalesEveryElement()
    {
        int[] values = [0, 1, 2, 3, 4];

        FileLocalTypeKernels.LaunchApplyKernel(values, 3, blockDimX: 5);

        Assert.Equal([1, 4, 7, 10, 13], values);
    }

    // The kernel names the type itself: the managed path has no copy and runs the user's method
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void LaunchScaleKernel_FileLocalStructInKernel_ScalesEveryElement(bool forceManaged)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");
        int[] values = [0, 1, 2, 3, 4];

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            FileLocalTypeKernels.LaunchScaleKernel(values, 3, blockDimX: 5);
        }

        Assert.Equal([1, 4, 7, 10, 13], values);
    }

    [SkippableFact]
    [Scenario(TestScenario.HappyPath)]
    public void LaunchBarrierScaleKernel_FileLocalStructAcrossBarrier_RunsOnTheNativePath()
    {
        int[] values = [0, 1, 2, 3, 4];

        FileLocalTypeKernels.LaunchBarrierScaleKernel(values, 3, blockDimX: 5);

        Assert.Equal([1, 4, 7, 10, 13], values);
    }

    // The managed copy of the kernel would hold the local across the barrier, which a generated file cannot name
    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void LaunchBarrierScaleKernel_ManagedPathOfAFileLocalStructAcrossBarrier_Throws()
    {
        Skip.If(IsCudaDialect, "The managed execution path exists only on the CPU dialects.");
        int[] values = [0, 1, 2, 3, 4];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var exception = Assert.Throws<NotSupportedException>(
                () => FileLocalTypeKernels.LaunchBarrierScaleKernel(values, 3, blockDimX: 5));

            Assert.Contains("BarrierScaleKernel cannot run on the managed path of the CPU dialect",
                exception.Message, StringComparison.Ordinal);
            Assert.Contains("'Kassyi.GSharp.Tests.Shared.Syntax.FileLocalScale'", exception.Message,
                StringComparison.Ordinal);
        }

        Assert.Equal([0, 1, 2, 3, 4], values);
    }

    // The division makes the managed path copy the kernel (GSH0119); the native path and the C# execution run the
    // user's method
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(KernelExecution.Default)]
    [InlineData(KernelExecution.CSharp)]
    public void LaunchDivideKernel_FileLocalStructAndNonConstantDivision_RunsOnTheNativeAndCSharpPaths(
        KernelExecution execution)
    {
        Skip.If(execution == KernelExecution.CSharp && IsCudaDialect,
            "The C# execution exists only on the CPU dialects.");
        int[] values = [7, 8, 9, 10, 11];

        using (GpuExecution.Use(execution))
        {
            FileLocalTypeKernels.LaunchDivideKernel(values, 3, blockDimX: 5);
        }

        Assert.Equal([21, 22, 30, 31, 32], values);
    }

    [SkippableFact]
    [Scenario(TestScenario.SadPath)]
    public void LaunchDivideKernel_ManagedPathOfAFileLocalStructAndDivision_ThrowsNamingTheDivision()
    {
        Skip.If(IsCudaDialect, "The managed execution path exists only on the CPU dialects.");
        int[] values = [7, 8, 9, 10, 11];

        using (GpuExecution.Use(KernelExecution.Managed))
        {
            var exception = Assert.Throws<NotSupportedException>(
                () => FileLocalTypeKernels.LaunchDivideKernel(values, 3, blockDimX: 5));

            Assert.Contains("DivideKernel cannot run on the managed path of the CPU dialect", exception.Message,
                StringComparison.Ordinal);
            Assert.Contains("an integer division or remainder", exception.Message, StringComparison.Ordinal);
            Assert.Contains("'Kassyi.GSharp.Tests.Shared.Syntax.FileLocalScale'", exception.Message,
                StringComparison.Ordinal);
        }

        Assert.Equal([7, 8, 9, 10, 11], values);
    }
}
