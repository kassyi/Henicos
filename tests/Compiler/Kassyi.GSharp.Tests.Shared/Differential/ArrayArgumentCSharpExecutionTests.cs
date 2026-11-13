#nullable enable
using System;
using System.Collections.Immutable;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// The C# execution of a launcher (<see cref="KernelExecution.CSharp"/>) runs the kernel body with the arguments the
/// caller passed: the launcher neither replaces an empty array with null nor rejects a null or empty one first.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public sealed class ArrayArgumentCSharpExecutionTests
{
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CSharpExecution_EmptyOptionalArray_IsNotNull()
    {
        int[] o = [-1];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableArrayKernel([], ArrayArgumentReads.IsNull, o);
        }

        Assert.Equal([0], o);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CSharpExecution_NullNonNullableArrayThatIsOnlyCompared_ReachesTheBody()
    {
        int[] o = [-1];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            ArrayArgumentNullEmptyDifferentialKernels.LaunchNonNullableArrayKernel(null!, ArrayArgumentReads.IsNull, o);
        }

        Assert.Equal([3], o);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CSharpExecution_EmptyNonNullableArray_ReadsLengthZero()
    {
        int[] o = [-1];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            ArrayArgumentNullEmptyDifferentialKernels.LaunchNonNullableArrayKernel([], ArrayArgumentReads.Length, o);
        }

        Assert.Equal([0], o);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CSharpExecution_DefaultSpan_ReachesTheBodyAsNull()
    {
        int[] o = [-1];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            ArrayArgumentNullEmptyDifferentialKernels.LaunchSpanKernel(default, ArrayArgumentReads.IsNull, o);
        }

        Assert.Equal([1], o);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CSharpExecution_EmptyImmutableArray_ReadsLengthZero()
    {
        int[] o = [-1];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            ArrayArgumentNullEmptyDifferentialKernels.LaunchImmutableKernel(
                ImmutableArray<int>.Empty, ArrayArgumentReads.Length, o);
        }

        Assert.Equal([0], o);
    }

    // An empty Memory<int>? keeps its value: HasValue is true and the span is not over a null array
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void CSharpExecution_EmptyNullableMemory_KeepsItsValue()
    {
        int[] o = [-1];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            ArrayArgumentNullEmptyDifferentialKernels.LaunchNullableMemoryKernel(
                new Memory<int>([]), ArrayArgumentReads.IsNull, o);
        }

        Assert.Equal([1], o);
    }
}
