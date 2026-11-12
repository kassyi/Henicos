using Kassyi.GSharp.Core;
using System;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class SpanKernel
{
    [GpuKernel]
    public static void Execute(Span<int> data)
    {
    }
}
