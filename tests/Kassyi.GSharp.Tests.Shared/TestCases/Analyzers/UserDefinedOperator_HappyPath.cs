using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static class MacroOps_UserDefinedOpTest
{
    [GpuDeviceFunction]
    public static void AddVec(ref CustomVec_UserDefinedOpTest res, CustomVec_UserDefinedOpTest a, CustomVec_UserDefinedOpTest b)
    {
        res.X = a.X + b.X;
    }
}

public struct CustomVec_UserDefinedOpTest
{
    public float X;

    [GpuOperatorMacro(typeof(MacroOps_UserDefinedOpTest), nameof(MacroOps_UserDefinedOpTest.AddVec))]
    public static CustomVec_UserDefinedOpTest operator +(CustomVec_UserDefinedOpTest a, CustomVec_UserDefinedOpTest b)
    {
        CustomVec_UserDefinedOpTest res = default;
        MacroOps_UserDefinedOpTest.AddVec(ref res, a, b);
        return res;
    }
}

// Declared here rather than reusing V2 from the SadPath file: test case files are compiled one at a time.
public struct HostVec_UserDefinedOpTest
{
    public float X;
    public float Y;

    public static HostVec_UserDefinedOpTest operator +(HostVec_UserDefinedOpTest a, HostVec_UserDefinedOpTest b) =>
        new HostVec_UserDefinedOpTest { X = a.X + b.X, Y = a.Y + b.Y };

    public static HostVec_UserDefinedOpTest operator *(HostVec_UserDefinedOpTest a, float s) =>
        new HostVec_UserDefinedOpTest { X = a.X * s, Y = a.Y * s };

    public static HostVec_UserDefinedOpTest operator -(HostVec_UserDefinedOpTest a) =>
        new HostVec_UserDefinedOpTest { X = -a.X, Y = -a.Y };

    public static HostVec_UserDefinedOpTest operator ++(HostVec_UserDefinedOpTest a) =>
        new HostVec_UserDefinedOpTest { X = a.X + 1, Y = a.Y + 1 };
}

public static partial class GSH_62_UserDefinedOperator_HappyPath
{
    [GpuKernel]
    public static void PrimitiveArithmeticKernel(float[] data)
    {
        int t = ThreadIndex.X;
        data[t] = (data[t] + 1.0f) * 0.5f;
    }

    [GpuKernel]
    public static void SystemNumericsVectorKernel(Vector2[] v)
    {
        int t = ThreadIndex.X;
        v[t] = v[t] + v[t];
    }

    [GpuKernel]
    public static void MacroOperatorKernel(CustomVec_UserDefinedOpTest[] items)
    {
        int t = ThreadIndex.X;
        items[t] = items[t] + items[t];
    }

    public static void HostOnlyMethod(HostVec_UserDefinedOpTest[] v)
    {
        v[0] = (v[0] + v[0]) * 0.5f;
        v[0] = -v[0];
        v[0]++;
    }
}
