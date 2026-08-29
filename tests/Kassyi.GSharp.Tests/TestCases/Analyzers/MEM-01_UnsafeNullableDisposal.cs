#nullable enable
using Kassyi.GSharp.Stdlib.Math;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class MEM_01_UnsafeNullableDisposal
{
    private GpuContext? GetContextOrNull() => null;

    public void Test()
    {
        GpuContext? ctx = GetContextOrNull();
        ctx.Dispose();
    }
}
