using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class GEN_18_VectorBuiltins
{
    [GpuKernel]
    public static void VectorBuiltinMapping(System.Numerics.Vector2[] v2, System.Numerics.Vector3[] v3, System.Numerics.Vector4[] v4, float[] result)
    {
        var localV2 = new System.Numerics.Vector2(1f, 2f);
        var localV3 = default(System.Numerics.Vector3);
        
        var x = v2[0].X + localV2.Y;
        var y = v3[0].Y + localV3.Z;
        var z = v4[0].Z + v4[0].W;
        
        result[0] = x + y + z;
    }
}
