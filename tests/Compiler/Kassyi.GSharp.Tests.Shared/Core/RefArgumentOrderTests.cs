#nullable enable
using System;
using System.Linq;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Runs <see cref="RefArgumentOrderKernels"/> on CUDA and on both CPU paths, native and managed (#788). The expected
/// values are what C# gives: the storage of a by-reference argument and the value of every other argument are fixed when
/// the argument is evaluated.
/// </summary>
[TestTarget(ExecutionTarget.GpuExecution)]
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class RefArgumentOrderTests : TranspilerTestBase
{
    private const int Threads = 32;

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_FieldOfElement_IndexWrittenLater_SelectsTheOldElement(bool forceManaged)
    {
        var cells = new RefOrderCell[Threads * 2];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchFieldOfElementIndexWrittenLaterKernel(cells, blockDimX: Threads));

        // Thread t adds 2t + 1000 to cells[2t].F
        Assert.Equal(Enumerable.Range(0, Threads * 2).Select(static k => k % 2 == 0 ? k + 1000 : 0),
            cells.Select(static c => c.F));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_IndexFromMemory_ChangedByCallee_SelectsTheElementOnce(bool forceManaged)
    {
        var cells = new RefOrderCell[Threads * 2];
        var idx = Enumerable.Range(0, Threads).ToArray();

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchIndexFromMemoryChangedByCalleeKernel(cells, idx, blockDimX: Threads));

        // idx[t] was t when the argument was evaluated; the callee's 63 - t does not move the write
        Assert.Equal(Enumerable.Range(0, Threads * 2).Select(static k => k < Threads ? 1000 : 0),
            cells.Select(static c => c.F));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Test_ArrayReplacedByLaterArgument_WritesTheOldArray(bool forceManaged, bool inField)
    {
        var data = new int[Threads];
        var other = new int[Threads];

        Run(forceManaged, () =>
        {
            if (inField)
            {
                RefArgumentOrderKernels.LaunchArrayFieldReplacedByLaterArgumentKernel(data, other,
                    blockDimX: Threads);
            }
            else
            {
                RefArgumentOrderKernels.LaunchArrayReplacedByLaterArgumentKernel(data, other, blockDimX: Threads);
            }
        });

        // The later argument is other.Length (32)
        Assert.All(data, static v => Assert.Equal(1000 + Threads, v));
        Assert.All(other, static v => Assert.Equal(0, v));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Test_IndexPassedByRef_IncrementedByCallee_SelectsTheOldElement(bool forceManaged, bool earlier)
    {
        var data = new int[Threads * 2];

        Run(forceManaged, () =>
        {
            if (earlier)
            {
                RefArgumentOrderKernels.LaunchIndexPassedByRefEarlierKernel(data, blockDimX: Threads);
            }
            else
            {
                RefArgumentOrderKernels.LaunchIndexPassedByRefLaterKernel(data, blockDimX: Threads);
            }
        });

        Assert.Equal(Enumerable.Range(0, Threads * 2).Select(static k => k % 2 == 0 ? 1000 : 0), data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ValueOfVariablePassedByRef_IsTheValueBeforeTheCall(bool forceManaged)
    {
        var result = new int[Threads];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchValueOfVariablePassedByRefKernel(result, blockDimX: Threads));

        Assert.All(result, static v => Assert.Equal(7, v));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ValueReadThroughRefParameter_IsTheValueBeforeTheCall(bool forceManaged)
    {
        var data = Enumerable.Range(0, Threads).ToArray();
        var result = new int[Threads];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchValueReadThroughRefParameterKernel(data, result, blockDimX: Threads));

        Assert.Equal(Enumerable.Range(0, Threads), result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_InParameter_SeesTheLaterWriteOfItsVariable(bool forceManaged)
    {
        var result = new int[Threads];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchInParameterSeesLaterWriteKernel(result, blockDimX: Threads));

        // p aliases x, which x++ made 2; q is the 1 that x++ returned
        Assert.All(result, static v => Assert.Equal(21, v));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_InParameterOfElement_SeesTheCalleesWrite(bool forceManaged)
    {
        var data = Enumerable.Range(0, Threads).ToArray();
        var result = new int[Threads];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchInParameterOfElementKernel(data, result, blockDimX: Threads));

        Assert.All(result, static v => Assert.Equal(5, v));
    }

    [SkippableTheory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_IntExpressionToLongParameter_PassesItsValue(bool forceManaged)
    {
        var result = new long[Threads];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchIntExpressionToLongParameterKernel(result, blockDimX: Threads));

        Assert.Equal(Enumerable.Range(0, Threads).Select(static t => (long)(t * 3 + 1)), result);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_NestedSuspendingCall_WritesIndexAfterTheElementIsFixed(bool forceManaged)
    {
        var data = new int[Threads * 2];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchNestedCallWritesIndexKernel(data, blockDimX: Threads));

        // Thread t adds 2t + 1000 to data[2t]
        Assert.Equal(Enumerable.Range(0, Threads * 2).Select(static k => k % 2 == 0 ? k + 1000 : 0), data);
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_NestedSuspendingCall_WritesEarlierValueArgument(bool forceManaged)
    {
        var result = new int[Threads];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchNestedCallWritesEarlierValueKernel(result, blockDimX: Threads));

        Assert.All(result, static v => Assert.Equal(71, v));
    }

    [SkippableTheory]
    [Scenario(TestScenario.Boundary)]
    [InlineData(false)]
    [InlineData(true)]
    public void Test_ValueFieldToInParameter_PassesACopyEvaluatedOnce(bool forceManaged)
    {
        var result = new int[Threads];

        Run(forceManaged, () =>
            RefArgumentOrderKernels.LaunchValueFieldToInParameterKernel(result, blockDimX: Threads));

        Assert.All(result, static v => Assert.Equal(23, v));
    }

    private void Run(bool forceManaged, Action launch)
    {
        Skip.If(forceManaged && IsCudaDialect, "The managed execution path exists only on the CPU dialects.");

        var previous = CpuKernelLoader.ForceManagedExecution;
        CpuKernelLoader.ForceManagedExecution = forceManaged;
        try
        {
            launch();
        }
        finally
        {
            CpuKernelLoader.ForceManagedExecution = previous;
        }
    }
}
