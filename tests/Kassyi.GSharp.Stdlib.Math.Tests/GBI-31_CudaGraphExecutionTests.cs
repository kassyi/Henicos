#nullable enable
using System;
using System.Numerics;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Xunit;

namespace Kassyi.GSharp.Stdlib.Math.Tests;

[Trait("Category", "GpuExecution")]
public class GBI_31_CudaGraphExecutionTests
{
    [SkippableFact]
    [Trait("FeatureId", "GBI-31")]
    public void Test_GBI31_CudaGraph_Add_Execution()
    {
        CudaCapability.EnsureGpuAvailable();

        Exception? threadEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                int numWords = 4;
                using var ctx = new GpuWorkspace(numWords, GpuExecutionEngine.GSharpAot);
                using var a = new GpuUIntBigInteger(ctx, numWords);
                using var b = new GpuUIntBigInteger(ctx, numWords);
                using var c = new GpuUIntBigInteger(ctx, numWords);
                using var d = new GpuUIntBigInteger(ctx, numWords);

                uint[] hostA = { 1, 2, 3, 4 };
                uint[] hostB = { 5, 6, 7, 8 };
                uint[] hostC = new uint[numWords];

                ctx.Provider.CopyHostToDevice(a.DevicePointer, hostA, numWords, ctx.Stream);
                ctx.Provider.CopyHostToDevice(b.DevicePointer, hostB, numWords, ctx.Stream);

                // Warmup: Perform the operations once before capturing to ensure all JIT compilation,
                // static constructors, and lazy memory allocations (e.g., RentTemp) are completed.
                // Implicit synchronizations during these first-time setup steps will otherwise break the capture.
                ctx.Provider.Add(ctx, a, b, c);
                ctx.Provider.CopyDeviceToDevice(d.DevicePointer, c.DevicePointer, numWords, ctx.Stream);
                CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(ctx.Stream));

                // Begin Capture on the context's stream
                ctx.Provider.BeginCapture(ctx.Stream);

                // Perform the operation that should be completely stream-capturable
                // If any internal operation uses stream 0 (IntPtr.Zero) or blockingly synchronizes,
                // cuStreamEndCapture will fail or the graph will be invalid.
                ctx.Provider.Add(ctx, a, b, c);
                // Also test CopyDeviceToDevice since it was identified as needing stream propagation
                ctx.Provider.CopyDeviceToDevice(d.DevicePointer, c.DevicePointer, numWords, ctx.Stream);

                // End Capture
                var graph = ctx.Provider.EndCapture(ctx.Stream);
                Assert.NotEqual(IntPtr.Zero, graph);

                // Instantiate and Launch
                var graphExec = ctx.Provider.InstantiateGraph(graph);
                Assert.NotEqual(IntPtr.Zero, graphExec);

                ctx.Provider.LaunchGraph(graphExec, ctx.Stream);
                CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(ctx.Stream));

                ctx.Provider.CopyDeviceToHost(hostC, d.DevicePointer, numWords, ctx.Stream);

                // Verify result (hostA + hostB = hostC)
                for (int i = 0; i < numWords; i++)
                {
                    Assert.Equal(hostA[i] + hostB[i], hostC[i]);
                }

                ctx.Provider.DestroyGraph(graph, graphExec);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [SkippableFact]
    [Trait("FeatureId", "GBI-31")]
    [Trait("Category", "Stress")]
    public void Test_GBI31_CudaGraph_StressLaunch()
    {
        CudaCapability.EnsureGpuAvailable();

        Exception? threadEx = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                int numWords = 1024;
                using var ctx = new GpuWorkspace(numWords, GpuExecutionEngine.GSharpAot);
                using var a = new GpuUIntBigInteger(ctx, numWords);
                using var b = new GpuUIntBigInteger(ctx, numWords);
                using var c = new GpuUIntBigInteger(ctx, numWords);

                // Simple initialization
                uint[] hostA = new uint[numWords];
                hostA[0] = 1;
                uint[] hostB = new uint[numWords];
                hostB[0] = 1;
                ctx.Provider.CopyHostToDevice(a.DevicePointer, hostA, numWords, ctx.Stream);
                ctx.Provider.CopyHostToDevice(b.DevicePointer, hostB, numWords, ctx.Stream);

                // Warmup before capture to resolve lazy allocations
                ctx.Provider.Add(ctx, a, b, c);
                ctx.Provider.CopyDeviceToDevice(a.DevicePointer, c.DevicePointer, numWords, ctx.Stream);
                CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(ctx.Stream));

                // Reset inputs back to initial state after warmup
                ctx.Provider.CopyHostToDevice(a.DevicePointer, hostA, numWords, ctx.Stream);
                ctx.Provider.CopyHostToDevice(b.DevicePointer, hostB, numWords, ctx.Stream);
                CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(ctx.Stream));

                ctx.Provider.BeginCapture(ctx.Stream);
                ctx.Provider.Add(ctx, a, b, c); // c = a + b
                ctx.Provider.CopyDeviceToDevice(a.DevicePointer, c.DevicePointer, numWords, ctx.Stream); // a = c
                var graph = ctx.Provider.EndCapture(ctx.Stream);
                var graphExec = ctx.Provider.InstantiateGraph(graph);

                // Launch the graph 1000 times to simulate stress and ensure no memory/resource leaks
                for (int i = 0; i < 1000; i++)
                {
                    ctx.Provider.LaunchGraph(graphExec, ctx.Stream);
                }

                CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(ctx.Stream));

                uint[] hostC = new uint[numWords];
                ctx.Provider.CopyDeviceToHost(hostC, c.DevicePointer, numWords, ctx.Stream);

                // 1 + 1 (1000 times accumulative addition) = 1 + 1000 = 1001
                Assert.Equal(1001u, hostC[0]);

                ctx.Provider.DestroyGraph(graph, graphExec);
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.Start();
        thread.Join();

        if (threadEx != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(threadEx).Throw();
        }
    }

    [Trait("FeatureId", "GBI-31")]
    [Trait("Category", "SadPath")]
    [Fact(Skip = "Corrupts global device context in sequential test execution")]
    public void Test_GBI31_CudaGraph_SadPath_ImplicitSyncFails()
    {
        CudaCapability.EnsureGpuAvailable();

        CudaException? capturedEx = null;

        // Run the capture and intentional failure in an isolated OS thread.
        // Failing a Thread-Local capture permanently pollutes the OS thread's capture state.
        // If we do this on the xUnit worker thread, subsequent tests will fail with CudaErrorStreamCaptureImplicit.
        var thread = new System.Threading.Thread(() =>
        {
            using var ctx = new GpuWorkspace(4, GpuExecutionEngine.GSharpAot);
            using var a = new GpuUIntBigInteger(ctx, 4);

            try
            {
                ctx.Provider.BeginCapture(ctx.Stream);
                uint[] hostData = new uint[4];
                // This intentionally uses Stream 0 to trigger an implicit sync error
                ctx.Provider.CopyDeviceToHost(hostData, a.DevicePointer, 4, IntPtr.Zero);
            }
            catch (CudaException ex)
            {
                capturedEx = ex;
            }
            finally
            {
                // Cleanup the broken capture state by forcing EndCapture which will return NULL/Error
                CudaDriverApi.cuStreamEndCapture(ctx.Stream, out _);
            }
        });
        thread.Start();
        thread.Join();

        Assert.NotNull(capturedEx);
        Assert.Equal(CUresult.CudaErrorStreamCaptureImplicit, capturedEx.Result);
    }
}
