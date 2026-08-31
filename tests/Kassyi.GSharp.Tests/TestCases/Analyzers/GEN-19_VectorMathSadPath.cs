using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class GEN_19_VectorMathSadPath
{
    [GpuKernel]
    public static void KernelWithMismatchedVectorAddition(Vector2 v2, Vector3 v3, Vector2[] output)
    {
        // Mismatched vector addition (Vector2 + Vector3) triggers Roslyn CS0019
        output[0] = v2 + v3;
    }

    [GpuKernel]
    public static void KernelWithUnsupportedType(Vector2[] input, Vector2[] output)
    {
        // String type usage inside GPU kernel triggers GSH0002 (StringNotSupported)
        string text = "unsupported_string_in_kernel";
        output[0] = input[0];
    }
}
