using System;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// An integer <c>/</c> or <c>%</c> by 0 throws <see cref="DivideByZeroException"/> as in C# (#718 (Array and span
/// bounds, integer division by zero, Nullable.Value, Math.Clamp and Math.Abs(MinValue) are not checked as in C#)): on
/// the native CPU build with the place of the division, on the managed path from the C# operator. <c>MinValue / -1</c>
/// gives the GPU result, <c>MinValue</c>, where x86 <c>idiv</c> would end the native process.
/// </summary>
/// <remarks>
/// The zero divisors are skipped on CUDA: the release CUDA build does not check them (only the checked build does), so
/// the result there is unspecified. The differential cases (<c>IntegerDivisionTrapDifferentialTests</c>) list the CUDA
/// cells as a known divergence.
/// </remarks>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
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

    // mutant: IntegerDivision.hpp without the -1 test (x86 idiv ends the native process)
    [SkippableFact]
    [Scenario(TestScenario.Boundary)]
    public void Quotient_MinValueByMinusOne_GivesTheGpuResult()
    {
        int[] a = [int.MinValue, int.MinValue];
        int[] d = [-1, 1];
        var o = new int[2];

        // The managed path runs the C# operator, which .NET makes throw OverflowException (implementation-defined)
        Run(forceManaged: false, () => IntegerDivisionCheckKernels.LaunchQuotientKernel(a, d, o, blockDimX: 2));

        Assert.Equal([int.MinValue, int.MinValue], o);
    }

    // mutant: IntegerDivision.hpp without the divisor check (the native launch ends the process)
    [SkippableTheory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Quotient_ZeroDivisor_ThrowsWithThePlaceOfTheDivision(bool forceManaged)
    {
        Skip.If(IsCudaDialect, "The release CUDA build does not check a divisor.");
        int[] a = [7, 8];
        int[] d = [1, 0];
        var o = new int[2];

        var exception = Assert.Throws<DivideByZeroException>(
            () => Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchQuotientKernel(a, d, o, blockDimX: 2))
        );

        Assert.Equal(7, o[0]);
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
        Skip.If(IsCudaDialect, "The release CUDA build does not check a divisor.");
        long[] a = [10L, 9L];
        long[] d = [-3L, 0L];
        var o = new long[2];

        var exception = Assert.Throws<DivideByZeroException>(
            () => Run(forceManaged, () => IntegerDivisionCheckKernels.LaunchDeviceFunctionRemainderKernel(a, d, o,
                blockDimX: 2))
        );

        if (!forceManaged)
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
        Skip.If(IsCudaDialect, "The release CUDA build does not check a divisor.");
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
