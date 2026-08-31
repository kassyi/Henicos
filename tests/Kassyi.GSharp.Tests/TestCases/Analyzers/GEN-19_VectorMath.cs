using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class GEN_19_VectorMath
{
    [GpuKernel]
    public static void VectorMathOperations(Vector2[] v2, Vector3[] v3, Vector4[] v4, float[] result)
    {
        var a2 = v2[0];
        var b2 = -a2;
        b2 += new Vector2(1f, 2f);
        var len2 = b2.Length();
        var norm2 = Vector2.Normalize(b2);
        var dot2 = Vector2.Dot(a2, norm2);

        var a3 = v3[0];
        var len3 = a3.Length();
        var dot3 = Vector3.Dot(a3, a3);

        var a4 = v4[0];
        var len4 = a4.Length();
        var dot4 = Vector4.Dot(a4, a4);

        result[0] = len2 + dot2 + len3 + dot3 + len4 + dot4;
    }
}
