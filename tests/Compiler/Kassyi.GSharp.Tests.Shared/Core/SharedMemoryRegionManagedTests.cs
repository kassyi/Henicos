#if GSHARP_CPU
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Tests.Shared.Helpers;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// The managed fallback hands every thread of a block a span into the block's shared memory. A region must never move
/// for a later declaration, and a repeated request with a larger count grows the region and keeps its contents.
/// </summary>
[TestTarget(ExecutionTarget.CpuExecution)]
[Category(TestCategory.Runtime)]
[Collection(TestCollections.GlobalStateSequential)]
public class SharedMemoryRegionManagedTests : TranspilerTestBase
{
    private static readonly Action<Action> s_runManaged = RunManaged;

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_LaterDeclaration_KeepsEarlierBuffer() =>
        SharedMemoryRegionScenarios.LaterDeclarationKeepsEarlierBuffer(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Managed_MixedElementTypes_KeepTheirOwnValues() =>
        SharedMemoryRegionScenarios.MixedElementTypes(s_runManaged);

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Managed_GrowingDeclaration_KeepsLargerCount() =>
        SharedMemoryRegionScenarios.GrowingDeclaration(s_runManaged);

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_Allocate_HeldSpan_SurvivesLaterDeclarations()
    {
        WithBlock(() =>
        {
            var held = ManagedGpuContext.AllocateSharedMemory<int>(100UL, 4);
            held[0] = 7;

            // Enough declarations to outgrow any buffer that is resized on demand
            for (ulong key = 101UL; key < 141UL; key++)
            {
                ManagedGpuContext.AllocateSharedMemory<long>(key, 64)[0] = (long)key;
            }

            Assert.Equal(7, held[0]);
            held[1] = 8;
            Assert.Equal(8, ManagedGpuContext.AllocateSharedMemory<int>(100UL, 4)[1]);
        });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Allocate_SmallerRepeatedRequest_ReturnsPrefixOfSameRegion()
    {
        WithBlock(() =>
        {
            var full = ManagedGpuContext.AllocateSharedMemory<int>(200UL, 8);
            full[7] = 3;

            var prefix = ManagedGpuContext.AllocateSharedMemory<int>(200UL, 4);
            Assert.Equal(4, prefix.Length);
            prefix[0] = 11;

            Assert.Equal(11, full[0]);
            Assert.Equal(3, ManagedGpuContext.AllocateSharedMemory<int>(200UL, 8)[7]);
        });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Allocate_SameKeyWithDifferentElementType_GetsSeparateRegion()
    {
        WithBlock(() =>
        {
            // One generic device function, two instantiations: CUDA gives each its own __shared__ array
            ManagedGpuContext.AllocateSharedMemory<int>(300UL, 4)[0] = 7;
            ManagedGpuContext.AllocateSharedMemory<float>(300UL, 4)[0] = 1.5f;

            Assert.Equal(7, ManagedGpuContext.AllocateSharedMemory<int>(300UL, 4)[0]);
            Assert.Equal(1.5f, ManagedGpuContext.AllocateSharedMemory<float>(300UL, 4)[0]);
        });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Allocate_NewGrid_ForgetsRegionSizes()
    {
        WithBlock(() =>
        {
            ManagedGpuContext.AllocateSharedMemory<int>(400UL, 4);

            ManagedGpuContext.InitializeGrid(1);
            var larger = ManagedGpuContext.AllocateSharedMemory<int>(400UL, 16);

            Assert.Equal(16, larger.Length);
        });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Allocate_NewGrid_ReusesLargeEnoughBuffer()
    {
        WithBlock(() =>
        {
            var first = ManagedGpuContext.AllocateSharedMemory<int>(450UL, 16);

            ManagedGpuContext.InitializeGrid(1);
            var second = ManagedGpuContext.AllocateSharedMemory<int>(450UL, 4);

            Assert.Equal(4, second.Length);
            Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(first), ref MemoryMarshal.GetReference(second)));
        });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Allocate_LargerRepeatedRequest_GrowsAndKeepsContents()
    {
        WithBlock(() =>
        {
            ManagedGpuContext.AllocateSharedMemory<int>(500UL, 4)[3] = 99;
            ManagedGpuContext.AllocateSharedMemory<int>(501UL, 4)[0] = 5;

            var grown = ManagedGpuContext.AllocateSharedMemory<int>(500UL, 8);
            grown[7] = 42;

            Assert.Equal(8, grown.Length);
            Assert.Equal(99, grown[3]);
            Assert.Equal(42, ManagedGpuContext.AllocateSharedMemory<int>(500UL, 8)[7]);
            Assert.Equal(5, ManagedGpuContext.AllocateSharedMemory<int>(501UL, 4)[0]);
        });
    }

    [Fact]
    [Scenario(TestScenario.Boundary)]
    public void Test_Allocate_LargerRequestWithinReusedBuffer_GrowsInPlace()
    {
        WithBlock(() =>
        {
            ManagedGpuContext.AllocateSharedMemory<int>(600UL, 16);

            ManagedGpuContext.InitializeGrid(1);
            var small = ManagedGpuContext.AllocateSharedMemory<int>(600UL, 4);
            small[0] = 13;
            var grown = ManagedGpuContext.AllocateSharedMemory<int>(600UL, 16);

            // The buffer kept from the previous grid already fits, so the region does not move
            Assert.Equal(16, grown.Length);
            Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(small), ref MemoryMarshal.GetReference(grown)));
            Assert.Equal(13, grown[0]);
        });
    }

    [Theory]
    [Scenario(TestScenario.HappyPath)]
    [InlineData("", 0, "", 12638153115695167455UL)]
    [InlineData("a.cs", 10, "M", 5490221886580183845UL)]
    [InlineData("P:/src/Kernels.cu.cs", 42, "ReduceKernel", 6238177414468702278UL)]
    [InlineData("\u65e5\u672c.cs", int.MaxValue, "\u30e1\u30bd\u30c3\u30c9", 1140269122134921303UL)]
    [InlineData("x.cs", -1, "M", 10450852572731352005UL)]
    public void Test_Declaration_SiteKey_IsPinnedFnv1A64(string path, int line, string member, ulong siteKey)
    {
        // The key is shared with the compiler's 64-bit key space; pin its exact value.
        WithBlock(() =>
        {
            var byCaller = new SharedMemory<int>(4, path, line, member);
            byCaller[0] = 9;

            Assert.Equal(9, new SharedMemory<int>(4, siteKey)[0]);
            Assert.Equal(0, new SharedMemory<int>(4, path, unchecked(line + 1), member)[0]);
        });
    }

    [Fact]
    [Scenario(TestScenario.HappyPath)]
    public void Test_RefIndexer_WriteThroughRef_IsVisibleOnSubsequentReads()
    {
        WithBlock(() =>
        {
            var smem = new SharedMemory<int>(4, 700UL);
            ref int slot = ref smem[2];
            slot = 77;

            Assert.Equal(77, smem[2]);

            WriteByRef(ref smem[2], 123);
            Assert.Equal(123, smem[2]);
        });
    }

    private static void WriteByRef(ref int target, int value) => target = value;

    private static void WithBlock(Action body) => RunOnFreshThread(() =>
    {
        try
        {
            ManagedGpuContext.SetThreadContext(new GpuThreadContext(0, 0, 0, 0, 0, 0, 32, 1, 1, 1, 1, 1));
            ManagedGpuContext.InitializeGrid(1);
            body();
        }
        finally
        {
            ManagedGpuContext.ClearThreadContext();
        }
    });

    private static void RunManaged(Action launch) => RunOnFreshThread(() =>
    {
        CpuKernelLoader.ForceManagedExecution = true;
        launch();
    });

    // The block buffer lives in thread-static state and is kept between launches. A fresh thread starts without one, so
    // the first declarations must grow it, which is what the regression needs.
    private static void RunOnFreshThread(Action body)
    {
        var failures = new ConcurrentQueue<Exception>();
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
        });
        thread.Start();
        thread.Join();

        if (failures.TryDequeue(out var failure))
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
#endif
