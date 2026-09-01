using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public struct GpuContext : IDisposable
{
    public void Dispose() { }
}

public static class SpreadDuplicationTest
{
    [GpuKernel]
    public static void KernelWithSpread(GpuContext ctx1, GpuContext ctx2)
    {
        Span<GpuContext> contexts = new GpuContext[] { ctx1, ctx2 };
        Span<GpuContext> newContexts = [.. contexts]; // Error: Ambiguous ownership duplication via Spread
    }
}
