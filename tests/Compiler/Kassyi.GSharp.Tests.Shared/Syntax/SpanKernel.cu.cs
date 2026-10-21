using Kassyi.GSharp.Core;
using System;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public partial class SpanKernel
{
    [GpuKernel]
    public void Execute(Span<int> data)
    {
    }
}
