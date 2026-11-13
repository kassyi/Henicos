#nullable enable
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
}
