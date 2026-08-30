using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public ref struct GpuSpan
{
    public void Dispose() { }
}

public static class InterfaceBoxingTest
{
    [GpuKernel]
    public static void KernelWithBoxing()
    {
        IDisposable resource = new GpuSpan(); // Error: Boxing allocation detected
    }
}
