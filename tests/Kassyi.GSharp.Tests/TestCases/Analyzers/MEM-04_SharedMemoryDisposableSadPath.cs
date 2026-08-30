// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public struct DisposableStruct : IDisposable
{
    public int Value;
    public void Dispose() { }
}

public struct ExplicitDisposableStruct : IDisposable
{
    public int Value;
    void IDisposable.Dispose() { }
}

public struct CustomDisposeStruct
{
    public int Value;
    public void Dispose() { }
}

public static class SharedMemoryDisposableSadPathKernel
{
    [GpuKernel]
    public static void ImplicitDisposableKernel()
    {
        var mem = new SharedMemory<DisposableStruct>(64);
    }

    [GpuKernel]
    public static void ExplicitDisposableKernel()
    {
        var mem = new SharedMemory<ExplicitDisposableStruct>(64);
    }

    [GpuKernel]
    public static void CustomDisposeKernel()
    {
        var mem = new SharedMemory<CustomDisposeStruct>(64);
    }

    [GpuKernel]
    public static void TargetTypedNewKernel()
    {
        SharedMemory<DisposableStruct> mem = new(64);
    }
}
