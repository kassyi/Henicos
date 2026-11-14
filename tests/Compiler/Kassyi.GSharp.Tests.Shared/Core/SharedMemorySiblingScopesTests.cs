#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="SharedMemorySiblingScopesKernels"/> on CUDA and on both CPU paths (#1084): the sibling scopes build
/// and each branch runs its own declaration.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Syntax)]
[Collection(TestCollections.GlobalStateSequential)]
public class SharedMemorySiblingScopesTests : TranspilerTestBase
{
    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false, 1, 10)]
    [InlineData(false, 0, 20)]
    [InlineData(true, 1, 10)]
    [InlineData(true, 0, 20)]
    public void BarrierAfterScopes_Execution_RunsTheDeclarationOfTheBranch(bool forceManaged, int branch, int offset)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemorySiblingScopesKernels.LaunchBarrierAfterScopesKernel(
            output, branch, blockDimX: 8));

        Assert.Equal(Enumerable.Range(offset, 8), output);
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false, 1, new[] { 11, 12, 13, 14, 15, 16, 17, 10 })]
    [InlineData(false, 0, new[] { 20, 21, 22, 23, 24, 25, 26, 27 })]
    [InlineData(true, 1, new[] { 11, 12, 13, 14, 15, 16, 17, 10 })]
    [InlineData(true, 0, new[] { 20, 21, 22, 23, 24, 25, 26, 27 })]
    public void BarrierInsideScope_Execution_RunsTheDeclarationOfTheBranch(
        bool forceManaged,
        int branch,
        int[] expected)
    {
        EnsureCapability(DialectCapabilities.SharedMemory);
        var output = new int[8];

        Run(forceManaged, () => SharedMemorySiblingScopesKernels.LaunchBarrierInsideScopeKernel(
            output, branch, blockDimX: 8));

        Assert.Equal(expected, output);
    }

    // The managed path exists only on the CPU dialects
    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        using (GpuExecution.Use(forceManaged ? KernelExecution.Managed : KernelExecution.Default))
        {
            launch();
        }
    }
}
