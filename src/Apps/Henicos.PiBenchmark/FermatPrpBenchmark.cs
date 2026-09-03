using System;
using System.Diagnostics;
using System.Threading;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;
using Henicos.PiBenchmark.Kernels;

namespace Henicos.PiBenchmark;

public class FermatPrpBenchmark
{
    public static unsafe void RunFermatTest(int iterations)
    {
        int E = 71428571;
        int numWords = (2 * E + 1 + 31) / 32;
        int outNumWords = numWords * 2 + 16;
        
        Console.WriteLine("==================================================");
        Console.WriteLine(" G# Native Fermat PRP Test (Global Cooley-Tukey)");
        Console.WriteLine("==================================================");
        Console.WriteLine($"E       = {E}");
        Console.WriteLine($"Digits  = {(E * Math.Log10(2)):N0}");
        Console.WriteLine($"Memory  = {(outNumWords * 4.0 / 1024 / 1024):F2} MB per buffer");
        Console.WriteLine("==================================================");

        int log2M = (int)Math.Ceiling(Math.Log2(numWords * 2));
        int M = 1 << log2M;
        long totalMemoryBytes = (long)M * 5 * sizeof(uint);
        
        CudaException.ThrowIfError(CudaDriverApi.cuInit(0));
        CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out int device, 0));
        CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out IntPtr context, 0, device));

        Console.WriteLine("Allocating Device Memory...");
        
        CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out IntPtr workspace, (nint)totalMemoryBytes));


        try
        {
            // Pointers for Double Buffering (mocked for now using workspace offsets if implemented in C++)
            // Here we just mock the loop dispatch to show the CUI progress logger.
            
            Console.WriteLine($"Starting {iterations} iterations of Fermat PRP Test...");
            var sw = Stopwatch.StartNew();
            
            // CUI Progress variables
            long totalOps = iterations;
            int lastLoggedSecond = -1;

            uint gridX = (uint)((M + 1023) / 1024);
            uint blockX = 1024;

            uint* srcPtr = (uint*)workspace;
            uint* dst1Ptr = srcPtr + M;
            uint* dst2Ptr = srcPtr + 2 * M;
            uint* twiddlesPtr = srcPtr + 3 * M;
            uint* tempPtr = srcPtr + 4 * M;

            for (int i = 0; i < iterations; i++)
            {
                // Dispatch the G# Kernels from the Host! (No Cooperative Group Limits)
                FermatPrpGpuKernelsWrapper.Launch_Kernel_Unpack16_Device((IntPtr)srcPtr, (IntPtr)dst1Ptr, (IntPtr)dst2Ptr, M, numWords, gridDimX: gridX, blockDimX: blockX);
                
                // Out-of-place bit-reverse to avoid memory thrashing/TDR
                FermatPrpGpuKernelsWrapper.Launch_Kernel_BitReverse_Device((IntPtr)dst1Ptr, (IntPtr)tempPtr, M, log2M, gridDimX: gridX, blockDimX: blockX);
                CudaDriverApi.cuMemcpyDtoD_v2((IntPtr)dst1Ptr, (IntPtr)tempPtr, (nint)((long)M * 4));

                FermatPrpGpuKernelsWrapper.Launch_Kernel_BitReverse_Device((IntPtr)dst2Ptr, (IntPtr)tempPtr, M, log2M, gridDimX: gridX, blockDimX: blockX);
                CudaDriverApi.cuMemcpyDtoD_v2((IntPtr)dst2Ptr, (IntPtr)tempPtr, (nint)((long)M * 4));

                // Global Cooley-Tukey Passes
                for (int pass = 1; pass <= log2M; pass++)
                {
                    // Pass 1 for Prime 1
                    FermatPrpGpuKernelsWrapper.Launch_Kernel_NttGlobalPass_Device((IntPtr)dst1Ptr, (IntPtr)twiddlesPtr, M, pass, FermatPrpGpuKernels.NTT_P1, FermatPrpGpuKernels.NTT_M1, gridDimX: gridX, blockDimX: blockX);
                    // Pass 2 for Prime 2
                    FermatPrpGpuKernelsWrapper.Launch_Kernel_NttGlobalPass_Device((IntPtr)dst2Ptr, (IntPtr)twiddlesPtr, M, pass, FermatPrpGpuKernels.NTT_P2, FermatPrpGpuKernels.NTT_M2, gridDimX: gridX, blockDimX: blockX);
                }

                FermatPrpGpuKernelsWrapper.Launch_Kernel_PackAndFermatReduce_Device((IntPtr)dst1Ptr, (IntPtr)dst2Ptr, (IntPtr)srcPtr, M, outNumWords, gridDimX: gridX, blockDimX: blockX);
                
                // --- CUI Progress Logger ---
                var elapsed = sw.Elapsed;
                if (elapsed.Seconds != lastLoggedSecond || i == iterations - 1)
                {
                    lastLoggedSecond = elapsed.Seconds;
                    double itersPerSec = (i + 1) / elapsed.TotalSeconds;
                    double remainingSecs = (iterations - (i + 1)) / (itersPerSec > 0 ? itersPerSec : 1);
                    TimeSpan eta = TimeSpan.FromSeconds(remainingSecs);
                    
                    Console.Write($"\r[Progress] {i + 1}/{iterations} | Speed: {itersPerSec:F2} iters/s | Elapsed: {elapsed:hh\\:mm\\:ss} | ETA: {eta:hh\\:mm\\:ss}");
                }
            }
            Console.WriteLine(); // Newline after progress finishes
            sw.Stop();
            Console.WriteLine($"Completed {iterations} iterations in {sw.ElapsedMilliseconds} ms");
        }
        finally
        {
            CudaDriverApi.cuMemFree_v2(workspace);
            CudaDriverApi.cuCtxDestroy_v2(context);
        }
    }
}
