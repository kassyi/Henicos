using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class Container : IDisposable
{
    private List<GpuDummyContext> _list = new();

    public void Dispose()
    {
        // Error: Undisposed collection elements in Dispose()
    }
}

public static class UndisposedCollectionFieldTest
{
    [GpuKernel]
    public static void KernelWithUndisposedCollection()
    {
        using var container = new Container();
    }
}

public struct GpuDummyContext : IDisposable
{
    public void Dispose() { }
}
