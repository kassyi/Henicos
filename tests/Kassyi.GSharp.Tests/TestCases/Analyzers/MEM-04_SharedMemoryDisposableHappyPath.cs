// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public struct ValidStruct
{
    public int X;
    public float Y;
}

public struct StructWithDisposeBoolParam
{
    public int Data;
    public void Dispose(bool disposing) { }
}

public static class SharedMemoryDisposableHappyPathKernel
{
    [GpuKernel]
    public static void PrimitiveSharedMemoryKernel()
    {
        var mem = new SharedMemory<int>(128);
    }

    [GpuKernel]
    public static void ValidStructSharedMemoryKernel()
    {
        var mem = new SharedMemory<ValidStruct>(64);
    }

    [GpuKernel]
    public static void DisposeWithBoolParamKernel()
    {
        SharedMemory<StructWithDisposeBoolParam> mem = new(32);
    }
}
