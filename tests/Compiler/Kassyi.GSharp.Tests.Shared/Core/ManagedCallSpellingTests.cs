using Xunit;
#if GSHARP_CPU
using Kassyi.GSharp.Tests.Shared.Helpers;
#endif

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The kernels of <see cref="ManagedCallSpellingKernels"/> on the native target (CPU or CUDA) and, on the CPU, on the
/// managed fallback, which spells these calls itself: both must give the values C# gives.
/// </summary>
[Category(TestCategory.Compiler)]
[Collection(TestCollections.GlobalStateSequential)]
public class ManagedCallSpellingTests : TranspilerTestBase
{
    private static readonly int[] s_values = [1, 2, 3, 4];

    // 2v (a field of a call result) + 20v (a field of a local) + 200v (a field of an array element)
    // + 2000v (a field of a parameter)
    private static readonly int[] s_inReceiverExpected = [2222, 4444, 6666, 8888];

    // The value of the neighbouring thread (lane ^ 1); the writing slot reads 0
    private static readonly int[] s_targetTypedSlotExpected = [2, 1, 4, 3];

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_InThisExtension_ReceiverFieldOfValueAndOfVariables()
    {
        var result = new int[4];

        ManagedCallSpellingKernels.LaunchInReceiverKernel(s_values, CreateCells(), result, blockDimX: 4);

        Assert.Equal(s_inReceiverExpected, result);
    }

    [Fact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_TargetTypedNew_GenericStructWithSharedMemory()
    {
        var result = new int[4];

        ManagedCallSpellingKernels.LaunchTargetTypedSlotKernel(s_values, result, blockDimX: 4);

        Assert.Equal(s_targetTypedSlotExpected, result);
    }

#if GSHARP_CPU
    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_InThisExtension_ReceiverFieldOfValueAndOfVariables()
    {
        var result = new int[4];
        var cells = CreateCells();

        ManagedKernelRun.Run(() => ManagedCallSpellingKernels.LaunchInReceiverKernel(s_values, cells, result,
            blockDimX: 4));

        Assert.Equal(s_inReceiverExpected, result);
    }

    [Fact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_TargetTypedNew_GenericStructWithSharedMemory()
    {
        var result = new int[4];

        ManagedKernelRun.Run(() => ManagedCallSpellingKernels.LaunchTargetTypedSlotKernel(s_values, result,
            blockDimX: 4));

        Assert.Equal(s_targetTypedSlotExpected, result);
    }
#endif

    // 100v in each cell
    private static SpellingCell[] CreateCells()
    {
        var cells = new SpellingCell[s_values.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i].Inner.V = s_values[i] * 100;
        }

        return cells;
    }
}
