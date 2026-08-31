// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Syntax;

public struct TestExtStruct
{
    public int Value;
    public int Factor;
}

public static class TestExtStructExtensions
{
    [GpuDeviceFunction]
    public static void Clear(this ref TestExtStruct s)
    {
        s.Value = 0;
        s.Factor = 0;
    }

    [GpuDeviceFunction]
    public static void Modify(this ref TestExtStruct s, int delta)
    {
        s.Value += delta;
    }

    [GpuDeviceFunction]
    public static int MultiplyAndAdd(this TestExtStruct s, int a, int b)
    {
        return s.Value * s.Factor + a + b;
    }

    [GpuDeviceFunction]
    public static int GenericIdentity(this TestExtStruct s, int input)
    {
        return input;
    }
}

public static class SYN_39_45_OperatorsAndInterfacesKernels
{
    // SYN-44: 拡張メソッド ('this' 引数)
    [GpuKernel]
    public static void ExtensionMethodKernel(int[] valIn, int[] multOut, int[] genericOut)
    {
        int tid = ThreadIndex.X;

        TestExtStruct s = default;
        s.Value = valIn[tid];
        s.Factor = 3;

        s.Modify(5); // ref extension method: s.Value becomes valIn[tid] + 5

        multOut[tid] = s.MultiplyAndAdd(10, 20); // (valIn[tid] + 5) * 3 + 10 + 20

        genericOut[tid] = s.GenericIdentity(valIn[tid] * 100);

        s.Clear(); // ref extension method: resets to 0
        if (s.Value != 0)
        {
            multOut[tid] = -1;
        }
    }
}
