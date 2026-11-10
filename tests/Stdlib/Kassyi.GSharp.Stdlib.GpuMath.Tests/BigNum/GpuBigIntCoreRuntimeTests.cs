using System.Collections.Concurrent;
using static Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.GpuBigIntCoreTestHelpers;

namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum;

[TestTarget(ExecutionTarget.GpuExecution)]
[Collection(TestCollections.GlobalStateSequential)]
public class GpuBigIntCoreRuntimeTests
{
    public GpuBigIntCoreRuntimeTests()
    {
        CudaCapability.EnsureGpuAvailable();
        // CUDA context is initialized automatically upon first kernel launch if supported.
        // Do not initialize here to prevent xUnit discovery crashes on non-GPU CI runners.
    }

    [SkippableFact]
    [Category(TestCategory.Alignment)]
    public void MemoryPool_MultiThreadedAllocation_ExecutesCorrectlyWithoutDataRace()
    {
        CudaCapability.EnsureGpuAvailable();
        // Stress test the internal pools (e.g. _carryBufferPool, _tempPool) to expose concurrency issues
        // across multiple concurrent threads sharing the same GpuWorkspace instance.
        var exceptions = new ConcurrentBag<Exception>();
        using var sharedCtx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);

        Parallel.For(0, 100, i =>
        {
            try
            {
                uint valA = (uint)i + 1;
                uint valB = (uint)i + 2;
                using var a = new GpuBigUInt(sharedCtx, new uint[] { valA, 0, 0, 0 });
                using var b = new GpuBigUInt(sharedCtx, new uint[] { valB, 0, 0, 0 });
                // a + b has 4 words + 1 bit (5 words); c * a then 5 + 4 words
                using var c = new GpuBigUInt(sharedCtx, 5);
                using var d = new GpuBigUInt(sharedCtx, 9);

                c.Value = a + b;
                d.Value = c * a;

                sharedCtx.Provider.SynchronizeStream(sharedCtx.Stream);

                var hostC = c.ToHostArray();
                var hostD = d.ToHostArray();
                Assert.Equal(valA + valB, hostC[0]);
                Assert.Equal(0u, hostC[1]);
                Assert.Equal((valA + valB) * valA, hostD[0]);
                Assert.Equal(0u, hostD[1]);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [SkippableFact]
    [Category(TestCategory.Runtime)]
    public void KernelFusion_ChainedArithmeticExpression_MatchesBigIntegerReference()
    {
        CudaCapability.EnsureGpuAvailable();
        // Expose missing kernel fusion & temporary memory leaks in ExpressionEvaluator.
        using var ctx = new GpuWorkspace(16, GpuExecutionEngine.GSharpAot);
        using var a = new GpuBigUInt(ctx, new uint[] { 100, 0, 0, 0 });
        using var b = new GpuBigUInt(ctx, new uint[] { 200, 0, 0, 0 });
        using var c = new GpuBigUInt(ctx, new uint[] { 300, 0, 0, 0 });
        using var result = new GpuBigUInt(ctx, 9); // (4 words + 1 bit) * 4 words

        // (a + b) * c
        result.Value = (a + b) * c;

        var expected = BigInteger.Parse("90000");
        Assert.Equal(expected, ToBigInteger(result.ToHostArray()));
    }

    [SkippableFact]
    [Category(TestCategory.Stress)]
    public void ConcurrentAccess_MultipleThreads_ExecutesWithoutDataRace()
    {
        CudaCapability.EnsureGpuAvailable();
        using var scope = new GpuKernelLoader.CudaContextScope();
        var engine = GpuExecutionEngine.GSharpAot;
        using var context = new GpuWorkspace(1024, engine);

        var arrA = GenerateRandomArray(256, 42);
        var arrB = GenerateRandomArray(256, 43);

        using var gpuA = new GpuBigUInt(context, arrA);
        using var gpuB = new GpuBigUInt(context, arrB);

        // Compute expected single-threaded baseline result
        using var expectedGpu = new GpuBigUInt(context, 256);
        expectedGpu.Value = gpuA + gpuB;
        var expectedHostResult = expectedGpu.ToHostArray();

        // Execute 100 concurrent Add operations modifying different targets
        // but reading from the same gpuA / gpuB to ensure context/stream stability
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 100, i =>
        {
            try
            {
                // Ensure each worker thread has the CUDA context bound
                using var threadScope = new GpuKernelLoader.CudaContextScope();

                using var gpuResult = new GpuBigUInt(context, 256);
                gpuResult.Value = gpuA + gpuB;

                var hostResult = gpuResult.ToHostArray();
                Assert.Equal(256, hostResult.Length);
                Assert.Equal(expectedHostResult, hostResult);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }
}
