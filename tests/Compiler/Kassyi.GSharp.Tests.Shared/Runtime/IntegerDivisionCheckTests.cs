using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// An integer <c>/</c> or <c>%</c> by 0 throws <see cref="DivideByZeroException"/> as in C# (#718 (Array and span
/// bounds, integer division by zero, Nullable.Value, Math.Clamp and Math.Abs(MinValue) are not checked as in C#)): on
/// the native CPU build and on CUDA with the place of the division, on the managed path from the C# operator.
/// <c>MinValue / -1</c> gives the GPU result, <c>MinValue</c>, on every path: x86 <c>idiv</c> would end the native
/// process, and .NET would throw <see cref="OverflowException"/> on the managed path.
/// </summary>
/// <remarks>
/// Every CUDA build checks the divisor; the failing thread records the error and exits (sm_70 and later), so no test
/// faults the GPU. The process has one device error slot, so the class runs alone
/// (<see cref="TestCollections.DeviceExclusive"/>).
/// </remarks>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.DeviceExclusive)]
public class IntegerDivisionCheckTests : TranspilerTestBase
{
    private const string KernelFile = "IntegerDivisionCheckKernels.cu.cs";

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Quotient_NonZeroDivisors_GivesTheCSharpQuotients(bool forceManaged)
    {
        int[] a = [7, -7, 100, int.MaxValue];
        int[] d = [2, 2, -3, 7];
        var o = new int[4];

        Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchQuotientKernel(a, d, o, blockDimX: 4));

        Assert.Equal([3, -3, -33, int.MaxValue / 7], o);
    }

    // mutant: IntegerDivision.hpp without the -1 test (x86 idiv ends the native process); ManagedDivision.Plan that
    // answers null (.NET throws OverflowException on the managed path)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Quotient_MinValueByMinusOne_GivesTheGpuResult(bool forceManaged)
    {
        int[] a = [int.MinValue, int.MinValue];
        int[] d = [-1, 1];
        var o = new int[2];

        Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchQuotientKernel(a, d, o, blockDimX: 2));

        Assert.Equal([int.MinValue, int.MinValue], o);
    }

    // A device function has the same result as a kernel body: its managed version calls the managed form (#711)
    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Remainder_MinValueByMinusOneInDeviceFunction_IsZero(bool forceManaged)
    {
        long[] a = [long.MinValue, long.MinValue];
        long[] d = [-1L, 3L];
        var o = new long[] { 7L, 7L };

        Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchDeviceFunctionRemainderKernel(a, d, o,
            blockDimX: 2));

        Assert.Equal([0L, long.MinValue % 3L], o);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedCompound_MinValueByMinusOne_GivesTheGpuResult(bool forceManaged)
    {
        int[] a = [int.MinValue, int.MinValue, 9];
        int[] d = [-1, 2, -1];

        Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchSignedCompoundKernel(a, d, blockDimX: 3));

        Assert.Equal([int.MinValue, int.MinValue / 2, -9], a);
    }

    // mutant: IntegerDivision.hpp without the divisor check (the native launch ends the process; CUDA throws nothing)
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Quotient_ZeroDivisor_ThrowsWithThePlaceOfTheDivision(bool forceManaged)
    {
        int[] a = [10, 99, 30, 40];
        int[] d = [1, 0, 3, 4];
        var o = new int[4];

        var exception = Assert.Throws<DivideByZeroException>(
            () => Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchQuotientKernel(a, d, o, blockDimX: 4))
        );

        // Only the failing thread stops (#1714): the threads before and after it write their quotients
        // mutant: a native launcher that ends the launch at the first failure leaves o[2] and o[3] at 0
        Assert.Equal([10, 0, 10, 10], o);
        if (!forceManaged)
        {
            Assert.Equal(KernelFile + "(14,16)", exception.Data["GSharp.SourceLocation"]);
        }
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Remainder_ZeroDivisorInDeviceFunction_NamesThePlaceInTheMessage(bool forceManaged)
    {
        long[] a = [10L, 9L];
        long[] d = [-3L, 0L];
        var o = new long[2];

        var exception = Assert.Throws<DivideByZeroException>(
            () => Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchDeviceFunctionRemainderKernel(a, d, o,
                blockDimX: 2))
        );

        // A CUDA device function reports its place through a message ID, not the text
        if (!forceManaged && !IsCudaDialect)
        {
            Assert.Equal("Attempted to divide by zero. at " + KernelFile + "(33,54)", exception.Message);
        }

        Assert.Equal(1L, o[0]);
    }

    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Compound_ZeroDivisor_ThrowsAndLeavesTheTarget(bool forceManaged)
    {
        uint[] a = [9u, 9u];
        uint[] d = [3u, 0u];

        Assert.Throws<DivideByZeroException>(
            () => Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchCompoundKernel(a, d, blockDimX: 2))
        );

        Assert.Equal([3u, 9u], a);
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
