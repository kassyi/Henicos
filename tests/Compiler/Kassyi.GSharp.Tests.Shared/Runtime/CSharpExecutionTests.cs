#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Tests.Shared.Core;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Verifies the C# execution of the generated launchers (<see cref="KernelExecution.CSharp"/>): the kernel's own method
/// runs once per GPU thread, one after another, and a kernel that needs the other threads throws instead.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class CSharpExecutionTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_CSharpScope_RunsTheThreadsInLinearOrderWithTheirCoordinates()
    {
        var order = new int[8];
        var next = new int[1];
        var coordinates = new int[8];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            CSharpExecutionKernels.LaunchRecordOrderKernel(order, next, coordinates, gridDimX: 2, blockDimX: 2,
                blockDimY: 2);
        }

        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], order);
        Assert.Equal(8, next[0]);
        // thread (x, y) of block (b, 0): x + 10 y + 100 b
        Assert.Equal([0, 1, 10, 11, 100, 101, 110, 111], coordinates);
    }

    // The exception comes from the kernel's own C# method, as C# throws it: the managed path and the native and CUDA
    // launches report an out-of-range index through G# messages instead
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Launch_CSharpScopeIndexPastTheEnd_ThrowsTheExceptionOfTheKernelMethod()
    {
        var expected = Assert.Throws<IndexOutOfRangeException>(static () => ReadPastTheEnd(new int[2]));
        var data = new[] { 1, 2 };
        int[] at = [5];
        var result = new int[1];

        var ex = Assert.Throws<IndexOutOfRangeException>(() =>
        {
            using (GpuExecution.Use(KernelExecution.CSharp))
            {
                CSharpExecutionKernels.LaunchReadAtKernel(data, at, result);
            }
        });

        Assert.Equal(expected.Message, ex.Message);
        // The top frame is the kernel method, not the state machine of the managed path
        var lines = (ex.StackTrace ?? "").AsSpan().EnumerateLines();
        Assert.True(lines.MoveNext(), "The exception has no stack trace.");
        Assert.Contains(
            $"{nameof(CSharpExecutionKernels)}.{nameof(CSharpExecutionKernels.ReadAtKernel)}(",
            lines.Current,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("__gsharp_statemachine", ex.StackTrace);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_CSharpScopeValueKernel_ReturnsTheValueOfEachThread()
    {
        int[] result;
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            result = CSharpExecutionKernels.LaunchReturnLinearKernel(3, gridDimX: 3, blockDimX: 4, blockDimY: 2);
        }

        Assert.Equal(Enumerable.Range(0, 24).Select(static i => i * 3).ToArray(), result);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_CSharpScopeRefAndOut_WritesTheValuesBackAfterTheLastThread()
    {
        var total = 10;
        int last;
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            CSharpExecutionKernels.LaunchRefOutKernel(ref total, out last, gridDimX: 2, blockDimX: 3);
        }

        Assert.Equal(16, total);
        Assert.Equal(5, last);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_CSharpScopeSpans_PassesTheSpansToEachThread()
    {
        var data = new int[6];
        int[] source = [1, 2, 3, 4, 5, 6];
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            CSharpExecutionKernels.LaunchSpanKernel(data.AsSpan(), source, blockDimX: 6);
        }

        Assert.Equal([3, 6, 9, 12, 15, 18], data);
    }

    // The element type holds references, which a span made from a pointer does not allow
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Launch_CSharpScopeSpansOfStructsWithArrays_PassesTheElements()
    {
        var bags = new[]
        {
            new ConvertBag { N = 1, Data = [3, 0] },
            new ConvertBag { N = 2, Data = [7, 0] }
        };
        var o = new int[2];

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            InArrayCopyKernels.LaunchReadOnlySpanBagKernel(bags, o, blockDimX: 2);
            CSharpExecutionKernels.LaunchSpanBagKernel(bags.AsSpan(), blockDimX: 2);
        }

        Assert.Equal([3, 7], o);
        // The C# call writes the caller's arrays: the launchers' copies do not apply
        Assert.Equal([77, 0], bags[0].Data);
        Assert.Equal([77, 78], new[] { bags[0].N, bags[1].N });
    }

    // Static shared memory in a device function of another assembly: its export record says so, and the launcher
    // throws before any thread runs
    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Launch_CSharpScopeLibraryFunctionWithSharedMemory_ThrowsBeforeRunning()
    {
        var data = new[] { 7, 7 };

        var ex = Assert.Throws<CSharpExecutionUnsupportedException>(() =>
        {
            using (GpuExecution.Use(KernelExecution.CSharp))
            {
                CSharpExecutionKernels.LaunchLibrarySharedMemoryKernel(data, blockDimX: 2);
            }
        });

        Assert.Contains("it reaches LibMath.StaticTile() of another assembly, which uses SharedMemory<T>", ex.Message);
        Assert.Equal([7, 7], data);
    }

    // The generated launcher throws before any thread runs: the array keeps its values
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData(nameof(CSharpExecutionKernels.BarrierKernel), "Gpu.SyncThreads()")]
    [InlineData(nameof(CSharpExecutionKernels.BarrierThroughDeviceFunctionKernel), "Gpu.SyncThreads()")]
    [InlineData(nameof(CSharpExecutionKernels.SharedMemoryKernel), "SharedMemory<T>")]
    public void Launch_CSharpScopeKernelNeedsOtherThreads_ThrowsBeforeRunning(string kernel, string construct)
    {
        var data = new[] { 7, 7, 7, 7 };

        var ex = Assert.Throws<CSharpExecutionUnsupportedException>(() =>
        {
            using (GpuExecution.Use(KernelExecution.CSharp))
            {
                LaunchBlockKernel(kernel, data);
            }
        });

        Assert.Equal(
            $"G#: CSharpExecutionKernels.{kernel} cannot run under KernelExecution.CSharp: it reaches {construct}, " +
            "and the C# execution runs one GPU thread at a time. Launch it under KernelExecution.Default or " +
            "KernelExecution.Managed.",
            ex.Message);
        Assert.Equal([7, 7, 7, 7], data);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Launch_CSharpScope_LeavesNoThreadContextOnTheCaller()
    {
        var order = new int[2];
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            CSharpExecutionKernels.LaunchRecordOrderKernel(order, new int[1], new int[2], blockDimX: 2);
        }

        Assert.False(ManagedGpuContext.IsActive);
        Assert.False(ManagedThreadContext.IsCSharpExecution);
        Assert.Throws<GpuOnlyException>(static () => ThreadIndex.X);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Launch_TaskInsideTheCSharpScope_RunsAsCSharp()
    {
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            await Task.Yield();
            var ex = await Assert.ThrowsAsync<CSharpExecutionUnsupportedException>(static () => Task.Run(static () =>
                CSharpExecutionKernels.LaunchBarrierKernel(new int[2], blockDimX: 2)));
            Assert.Contains("cannot run under KernelExecution.CSharp", ex.Message);
        }
    }

    // The internal order of the differential tests: the threads run in the order it gives
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Launch_CustomThreadOrder_RunsTheThreadsInThatOrder()
    {
        var order = new int[4];
        using (CSharpKernelRunner.UseThreadOrder(static d => Enumerable.Range(0, d.ThreadCount).Reverse().ToArray()))
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            CSharpExecutionKernels.LaunchRecordOrderKernel(order, new int[1], new int[4], blockDimX: 4);
        }

        Assert.Equal([3, 2, 1, 0], order);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Launch_ThreadOrderNotAPermutation_Throws()
    {
        using (CSharpKernelRunner.UseThreadOrder(static d => new int[d.ThreadCount]))
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            var ex = Assert.Throws<InvalidOperationException>(static () =>
                CSharpExecutionKernels.LaunchRecordOrderKernel(new int[2], new int[1], new int[2], blockDimX: 2));
            Assert.Contains("not a permutation", ex.Message);
        }
    }

    private static int ReadPastTheEnd(int[] data) => data[data.Length];

    private static void LaunchBlockKernel(string kernel, int[] data)
    {
        switch (kernel)
        {
            case nameof(CSharpExecutionKernels.BarrierKernel):
                CSharpExecutionKernels.LaunchBarrierKernel(data, blockDimX: 4);
                break;
            case nameof(CSharpExecutionKernels.BarrierThroughDeviceFunctionKernel):
                CSharpExecutionKernels.LaunchBarrierThroughDeviceFunctionKernel(data, blockDimX: 4);
                break;
            default:
                CSharpExecutionKernels.LaunchSharedMemoryKernel(data, blockDimX: 4);
                break;
        }
    }
}
