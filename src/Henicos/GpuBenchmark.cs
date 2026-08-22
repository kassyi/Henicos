using System;
using System.IO;
using BenchmarkDotNet.Attributes;
using Kassyi.GSharp;
using Kassyi.GSharp.Transpiler;
using Henicos;
using System.Runtime.InteropServices;
using Kassyi.Math.GpuBigInt;

namespace Henicos;

public class DummyKernelDef
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static uint HelperScale(uint x, uint scale)
    {
        return x * scale;
    }

    [GpuKernel]
    public static void VectorScale(uint[] T, int length)
    {
        for (int i = 0; i < length; i++)
        {
            T[i] = HelperScale(T[i], 2);
        }
    }
}

public class GpuBenchmark
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void VectorScaleCallback(IntPtr d_T, int length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate IntPtr AllocMemCallback(int bytes);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FreeMemCallback(IntPtr ptr);

    private VectorScaleCallback _gsharpKernel;
    private VectorScaleCallback _rawCudaKernel;
    private JitCompiledKernel<VectorScaleCallback> _gsharpModule;
    private JitCompiledKernel<VectorScaleCallback> _rawCudaModule;
    
    private AllocMemCallback _allocMem;
    private FreeMemCallback _freeMem;
    private JitCompiledKernel<AllocMemCallback> _memModule;

    [Params(1024, 1048576)] // 1K and 1M elements
    public int N { get; set; }

    private IntPtr _d_T;

    [GlobalSetup]
    public void Setup()
    {
        if (!CudaInterop.InitializeGPU(1000))
        {
            throw new Exception("GPU Initialization failed.");
        }

        string includePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\src\Kassyi.Math.GpuBigInt.Cuda"));

        // Helper module for memory management
        var memCode = @"
#include <cuda_runtime.h>
#include <stdint.h>
extern ""C"" __declspec(dllexport) void* AllocGpuMem(int bytes) {
    void* ptr = nullptr;
    cudaMalloc(&ptr, bytes);
    return ptr;
}
extern ""C"" __declspec(dllexport) void FreeGpuMem(void* ptr) {
    cudaFree(ptr);
}
";
        _memModule = JitCompiler.CompileAndLoad<AllocMemCallback>(memCode, "AllocGpuMem", includePath);
        _allocMem = _memModule.Kernel;
        var freeMemHandle = NativeLibrary.GetExport(_memModule.NativeHandle, "FreeGpuMem");
        _freeMem = Marshal.GetDelegateForFunctionPointer<FreeMemCallback>(freeMemHandle);

        _d_T = _allocMem(N * 4); // Allocate uint array

        // 1. Generate G# AST code
        var gsharpAstCode = TranspilerRunner.GenerateKernelCode(typeof(DummyKernelDef).Assembly.Location, nameof(DummyKernelDef.VectorScale));
        
        var gsharpCuCode = $@"
#include <cuda_runtime.h>
#include <stdint.h>

__global__ void Device_GSharpKernel(uint32_t* T, int length) {{
{gsharpAstCode}
}}

extern ""C"" __declspec(dllexport) void RunGSharp(uint32_t* d_T, int length) {{
    Device_GSharpKernel<<<1, 1>>>(d_T, length);
    cudaDeviceSynchronize();
}}
";

        var rawCuCode = @"
#include <cuda_runtime.h>
#include <stdint.h>

__global__ void Device_RawKernel(uint32_t* T, int length) {
    for(int i = 0; i < length; i++) {
        T[i] = T[i] * 2;
    }
}

extern ""C"" __declspec(dllexport) void RunRaw(uint32_t* d_T, int length) {
    Device_RawKernel<<<1, 1>>>(d_T, length);
    cudaDeviceSynchronize();
}
";
        _gsharpModule = JitCompiler.CompileAndLoad<VectorScaleCallback>(gsharpCuCode, "RunGSharp", includePath);
        _gsharpKernel = _gsharpModule.Kernel;

        _rawCudaModule = JitCompiler.CompileAndLoad<VectorScaleCallback>(rawCuCode, "RunRaw", includePath);
        _rawCudaKernel = _rawCudaModule.Kernel;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_d_T != IntPtr.Zero)
        {
            _freeMem(_d_T);
            _d_T = IntPtr.Zero;
        }
        _gsharpModule?.Dispose();
        _rawCudaModule?.Dispose();
        _memModule?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void RunRawCudaSequential()
    {
        _rawCudaKernel(_d_T, N);
    }

    [Benchmark]
    public void RunGSharpSequential()
    {
        _gsharpKernel(_d_T, N);
    }
}
