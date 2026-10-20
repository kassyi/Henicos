using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct V2
{
    public float X;
    public float Y;

    public static V2 operator +(V2 a, V2 b) => new V2 { X = a.X + b.X, Y = a.Y + b.Y };
    public static V2 operator *(V2 a, float s) => new V2 { X = a.X * s, Y = a.Y * s };
    public static V2 operator -(V2 a) => new V2 { X = -a.X, Y = -a.Y };
    public static V2 operator ++(V2 a) => new V2 { X = a.X + 1, Y = a.Y + 1 };
}

public static partial class GSH_62_UserDefinedOperator_SadPath
{
    [GpuKernel]
    public static void BinaryKernel(V2[] v)
    {
        int t = ThreadIndex.X;
        v[t] = (v[t] + v[t]) * 0.5f;
    }

    [GpuKernel]
    public static void CompoundKernel(V2[] v)
    {
        int t = ThreadIndex.X;
        V2 a = v[t];
        a += v[t];
        v[t] = a;
    }

    [GpuKernel]
    public static void UnaryKernel(V2[] v)
    {
        int t = ThreadIndex.X;
        v[t] = -v[t];
    }

    [GpuKernel]
    public static void IncrementKernel(V2[] v)
    {
        int t = ThreadIndex.X;
        V2 a = v[t];
        a++;
        v[t] = a;
    }

    [GpuDeviceFunction]
    public static V2 AddInDevice(V2 a, V2 b)
    {
        return a + b;
    }
}
