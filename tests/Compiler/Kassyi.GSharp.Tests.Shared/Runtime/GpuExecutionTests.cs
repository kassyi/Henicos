#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// Verifies the scope of <see cref="GpuExecution.Use"/> (restoring, nesting, the asynchronous flow), the managed
/// selection, and the run-time rejection of the operations that need the other GPU threads under
/// <see cref="KernelExecution.CSharp"/>.
/// </summary>
[Category(TestCategory.Runtime)]
[TestTarget(ExecutionTarget.CpuExecution)]
public class GpuExecutionTests
{
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Current_NoScope_IsDefault()
    {
        Assert.Equal(KernelExecution.Default, GpuExecution.Current);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Use_Disposed_RestoresThePreviousSelection()
    {
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            Assert.Equal(KernelExecution.CSharp, GpuExecution.Current);
        }

        Assert.Equal(KernelExecution.Default, GpuExecution.Current);
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Use_Nested_RestoresEachOuterSelection()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            using (GpuExecution.Use(KernelExecution.CSharp))
            {
                using (GpuExecution.Use(KernelExecution.Default))
                {
                    Assert.Equal(KernelExecution.Default, GpuExecution.Current);
                }

                Assert.Equal(KernelExecution.CSharp, GpuExecution.Current);
            }

            Assert.Equal(KernelExecution.Managed, GpuExecution.Current);
        }

        Assert.Equal(KernelExecution.Default, GpuExecution.Current);
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public async Task Use_TaskRunAndAwaitInsideTheScope_SeeTheSelection()
    {
        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            Assert.Equal(KernelExecution.CSharp, await Task.Run(static () => GpuExecution.Current));
            await Task.Delay(1);
            Assert.Equal(KernelExecution.CSharp, GpuExecution.Current);
            var seen = KernelExecution.Default;
            var thread = new Thread(() => seen = GpuExecution.Current);
            thread.Start();
            thread.Join();
            Assert.Equal(KernelExecution.CSharp, seen);
        }
    }

    // A thread that started before the scope keeps its own flow: the scope never leaks into it
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Use_ThreadStartedOutsideTheScope_DoesNotSeeTheSelection()
    {
        using var scopeOpen = new ManualResetEventSlim();
        using var read = new ManualResetEventSlim();
        var seen = KernelExecution.CSharp;
        var thread = new Thread(() =>
        {
            scopeOpen.Wait();
            seen = GpuExecution.Current;
            read.Set();
        });
        thread.Start();

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            scopeOpen.Set();
            read.Wait();
        }

        thread.Join();
        Assert.Equal(KernelExecution.Default, seen);
    }

    // A scope that Use did not return (default) restores nothing
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Dispose_DefaultScope_KeepsTheOuterSelection()
    {
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            default(GpuExecutionScope).Dispose();

            Assert.Equal(KernelExecution.Managed, GpuExecution.Current);
        }
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Use_ValueOutsideTheEnumeration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(static () => GpuExecution.Use((KernelExecution)3));
        Assert.Equal(KernelExecution.Default, GpuExecution.Current);
    }

    // The managed selection: the managed path although the library exists
    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void ShouldUseManagedExecution_ManagedScope_IsTrueForAnExistingLibrary()
    {
        var existing = typeof(GpuExecutionTests).Assembly.Location;

        Assert.False(CpuKernelLoader.ShouldUseManagedExecution(existing));
        using (GpuExecution.Use(KernelExecution.Managed))
        {
            Assert.True(CpuKernelLoader.ShouldUseManagedExecution(existing));
        }

        using (GpuExecution.Use(KernelExecution.CSharp))
        {
            Assert.False(CpuKernelLoader.ShouldUseManagedExecution(existing));
        }
    }

    // What the generator cannot see, such as a delegate, is rejected by the operation itself
    [Theory]
    [Scenario(TestScenario.SadPath)]
    [InlineData("Gpu.SyncThreads()")]
    [InlineData("Gpu.SyncGrid()")]
    [InlineData("Gpu.SyncGrid(body)")]
    [InlineData("Warp.ActiveMask()")]
    [InlineData("SharedMemory<T>")]
    public void Run_DelegateReachesAnOperationOfOtherThreads_ThrowsCSharpExecutionUnsupportedException(string construct)
    {
        Action<long> thread = construct switch
        {
            "Gpu.SyncThreads()" => static _ => Gpu.SyncThreads(),
            "Gpu.SyncGrid()" => static _ => Gpu.SyncGrid(),
            "Gpu.SyncGrid(body)" => static _ => Gpu.SyncGrid(static () => { }),
            "Warp.ActiveMask()" => static _ => Warp.ActiveMask(),
            _ => static _ => AllocateSharedMemory()
        };
        var ran = 0;

        var ex = Assert.Throws<CSharpExecutionUnsupportedException>(() => CSharpKernelRunner.Run(1, 1, 1, 2, 1, 1, id =>
        {
            ran++;
            thread(id);
        }));

        Assert.Equal(1, ran);
        Assert.Equal(
            $"G#: {construct} cannot run in a kernel launched under KernelExecution.CSharp, which runs the C# " +
            "body of the kernel one GPU thread at a time, so no other thread of the block or grid runs beside it. " +
            "Launch the kernel under KernelExecution.Default or KernelExecution.Managed.",
            ex.Message);
        Assert.False(ManagedThreadContext.IsCSharpExecution);
    }

    // Outside a C# execution a barrier keeps its behavior: the managed context lets it return
    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void SyncThreads_ManagedContextOutsideACSharpExecution_Returns()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                ManagedThreadContext.Set(new GpuThreadContext(0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1));
                Gpu.SyncThreads();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ManagedThreadContext.Clear();
            }
        });
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Fact]
    [Scenario(TestScenario.SadPath)]
    public void Run_ZeroDimension_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(static () =>
            CSharpKernelRunner.Run(1, 0, 1, 1, 1, 1, static _ => { }));
    }

    private static void AllocateSharedMemory()
    {
        var tile = new SharedMemory<int>(4);
        tile[0] = 1;
    }
}
