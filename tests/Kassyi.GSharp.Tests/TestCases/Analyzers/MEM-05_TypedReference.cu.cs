using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class MEM05_TypedReferenceTestCase
{
    [GpuKernel]
    public static void Kernel()
    {
        int ctx = 10;
        // Error: TypedReference operations are banned
        TypedReference tr = __makeref(ctx);
    }
}
