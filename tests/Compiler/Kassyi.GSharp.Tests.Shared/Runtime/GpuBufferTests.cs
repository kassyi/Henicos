using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Tests.Shared.Helpers;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

[Collection(TestCollections.DeviceExclusive)]
public class GpuBufferTests : TranspilerTestBase
{
    private const int WordsPerChunk = PinnedStagingBuffer.Size / sizeof(uint);

    // One element past 2 GiB of 4 KiB blocks: the byte count no longer fits int, the element count does
    private const int LargeBlockCount = (int)((1L << 31) / PageBlock.ByteSize) + 1;
    private const long LargeMinimumAvailableMemory = 8L << 30;
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    public GpuBufferTests()
    {
    }

    [SkippableTheory]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    public void Test_GpuBuffer_HappyPath_RoundTrip(int count)
    {
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();

        int[] sourceData = new int[count];
        for (int i = 0; i < count; i++)
        {
            sourceData[i] = i * 42;
        }

        using var buffer = new GpuBuffer<int>(count);
        Assert.Equal(count, buffer.Length);

        // When Length is 0, device pointer is technically unspecified but should not crash
        if (count > 0)
        {
            Assert.NotEqual(IntPtr.Zero, buffer.DevicePointer);
        }

        buffer.CopyToDevice(sourceData);

        int[] destData = new int[count];
        buffer.CopyToHost(destData);

        Assert.Equal(sourceData, destData);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.HappyPath)]
    public async Task CopyToDevice_IsOrderedBeforeSubsequentNonBlockingStreamWork()
    {
        CudaCapability.EnsureGpuAvailable();

        const int count = 64 * 1024;
        var initial = new uint[count];
        var updated = new uint[count];
        var readBack = new uint[count];
        Array.Fill(initial, 0x11111111u);

        using var buffer = CudaTestEnvironment.InContext(static () => new GpuBuffer<uint>(count));
        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            for (var iteration = 0; iteration < 32; iteration++)
            {
                var expected = 0xABCD0000u | (uint)iteration;
                Array.Fill(updated, expected);
                Array.Clear(readBack);

                buffer.CopyToDevice(initial);
                buffer.CopyToDevice(updated);

                await CudaHostTransfer.CopyToHostAsync(buffer.DevicePointer, readBack.AsMemory(), stream.Handle)
                    .AsTask().WaitAsync(CompletionTimeout);

                for (var i = 0; i < readBack.Length; i++)
                {
                    if (readBack[i] != expected)
                    {
                        Assert.Fail(
                            $"Iteration {iteration}, word {i} was 0x{readBack[i]:X8}, expected 0x{expected:X8}.");
                    }
                }
            }
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableTheory]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    [InlineData(1)]
    [InlineData(WordsPerChunk - 1)]
    [InlineData(WordsPerChunk)]
    [InlineData(WordsPerChunk + 1)]
    [InlineData((2 * WordsPerChunk) + 7)]
    public async Task CopyToDevice_AcrossStagingChunkBoundaries_RoundTripsOnNonBlockingStream(int count)
    {
        CudaCapability.EnsureGpuAvailable();

        var source = new uint[count];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = Pattern(i);
        }

        using var buffer = CudaTestEnvironment.InContext(() => new GpuBuffer<uint>(count));
        var stream = CudaTestEnvironment.InContext(static () => new CudaStream());
        try
        {
            buffer.CopyToDevice(source);

            var destination = new uint[count];
            await CudaHostTransfer.CopyToHostAsync(buffer.DevicePointer, destination.AsMemory(), stream.Handle)
                .AsTask().WaitAsync(CompletionTimeout);

            for (var i = 0; i < destination.Length; i++)
            {
                if (destination[i] != source[i])
                {
                    Assert.Fail($"Word {i} was 0x{destination[i]:X8}, expected 0x{source[i]:X8}.");
                }
            }
        }
        finally
        {
            CudaTestEnvironment.InContext(stream.Dispose);
        }
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.Boundary)]
    public void CopyToDevice_SequentialUploads_ReuseStagingBuffers()
    {
        CudaCapability.EnsureGpuAvailable();

        var source = new uint[4096];
        for (var i = 0; i < source.Length; i++)
        {
            source[i] = Pattern(i);
        }

        using var buffer = CudaTestEnvironment.InContext(static () => new GpuBuffer<uint>(4096));
        buffer.CopyToDevice(source);
        var allocatedBefore = PinnedStagingPool.AllocatedCount;

        for (var i = 0; i < 64; i++)
        {
            buffer.CopyToDevice(source);
        }

        Assert.Equal(allocatedBefore, PinnedStagingPool.AllocatedCount);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_GpuBuffer_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();

        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuBuffer<int>(-1));
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_GpuBuffer_OversizedSpan_ThrowsArgumentException()
    {
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();

        using var buffer = new GpuBuffer<int>(10);
        int[] oversizedData = new int[11];

        var ex1 = Assert.Throws<ArgumentException>(() => buffer.CopyToDevice(oversizedData));
        Assert.Contains("Source span is larger than the allocated device buffer", ex1.Message);

        var ex2 = Assert.Throws<ArgumentException>(() => buffer.CopyToHost(oversizedData));
        Assert.Contains("Destination span is larger than the allocated device buffer", ex2.Message);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    [Scenario(TestScenario.SadPath)]
    public void Test_GpuBuffer_UseAfterDispose_ThrowsObjectDisposedException()
    {
        using var _ctxScope = new GpuKernelLoader.CudaContextScope();

        var buffer = new GpuBuffer<int>(10);
        buffer.Dispose();

        int[] data = new int[5];

        Assert.Throws<ObjectDisposedException>(() => buffer.CopyToDevice(data));
        Assert.Throws<ObjectDisposedException>(() => buffer.CopyToHost(data));

        // Double dispose should be safe and not throw
        var exception = Record.Exception(() => buffer.Dispose());
        Assert.Null(exception);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.GpuExecution)]
    [Category(TestCategory.Runtime)]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    public void CopyToDeviceAndCopyToHost_BufferOf2GiBOrMore_RoundTrip()
    {
        CudaCapability.EnsureGpuAvailable();
        SkipUnlessLargeMemoryAvailable();

        using var buffer = CudaTestEnvironment.InContext(static () => new GpuBuffer<PageBlock>(LargeBlockCount));
        AssertLargeRoundTrip(buffer);
    }

    [SkippableFact]
    [TestTarget(ExecutionTarget.CpuExecution)]
    [Category(TestCategory.Runtime)]
    [Category(TestCategory.Stress)]
    [Scenario(TestScenario.Boundary)]
    public void CopyToDeviceAndCopyToHost_BufferOf2GiBOrMoreInCpuFallbackMode_RoundTrip()
    {
        SkipUnlessLargeMemoryAvailable();

        // ForceCpuFallback is AsyncLocal, so the override cannot leak into other tests.
        GpuKernelLoader.ForceCpuFallback = true;
        try
        {
            using var buffer = new GpuBuffer<PageBlock>(LargeBlockCount);
            AssertLargeRoundTrip(buffer);
        }
        finally
        {
            GpuKernelLoader.ForceCpuFallback = false;
        }
    }

    private static void SkipUnlessLargeMemoryAvailable() =>
        Skip.If(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes < LargeMinimumAvailableMemory,
            "A copy of 2 GiB or more needs about 4 GiB of free memory.");

    private static unsafe void AssertLargeRoundTrip(GpuBuffer<PageBlock> buffer)
    {
        var byteCount = (nuint)LargeBlockCount * PageBlock.ByteSize;
        var wordCount = checked((int)(byteCount / sizeof(uint)));
        var host = NativeMemory.Alloc(byteCount);
        try
        {
            var words = new Span<uint>(host, wordCount);
            for (var i = 0; i < words.Length; i++)
            {
                words[i] = Pattern(i);
            }

            var blocks = new Span<PageBlock>(host, LargeBlockCount);
            buffer.CopyToDevice(blocks);
            NativeMemory.Clear(host, byteCount);
            buffer.CopyToHost(blocks);

            for (var i = 0; i < words.Length; i++)
            {
                if (words[i] != Pattern(i))
                {
                    Assert.Fail($"Word {i} was 0x{words[i]:X8}, expected 0x{Pattern(i):X8}.");
                }
            }
        }
        finally
        {
            NativeMemory.Free(host);
        }
    }

    private static uint Pattern(int index) => unchecked(((uint)index * 2654435761u) + 17u);

    [InlineArray(ByteSize / sizeof(uint))]
    private struct PageBlock
    {
        public const int ByteSize = 4096;

        private uint _element0;
    }
}

