using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Emission;

public partial struct EmissionDeviceCounter
{
    public int X;

    [GpuDeviceFunction]
    public void Add(int v) => X += v;

    [GpuDeviceFunction]
    public readonly int Read() => X;
}

public static partial class InstanceDeviceFunctionShapeKernels
{
    [GpuDeviceFunction]
    private static int CallMutatingOnIn(in EmissionDeviceCounter p)
    {
        p.Add(5);
        return p.Read();
    }

    [GpuDeviceFunction]
    private static EmissionDeviceCounter Make(int x) => new() { X = x };

    [GpuKernel]
    public static void InstanceMethodsKernel(int[] output)
    {
        var c = new EmissionDeviceCounter { X = 10 };
        c.Add(2);
        int a = CallMutatingOnIn(in c);
        Make(7).Add(3);
        output[ThreadIndex.X] = c.Read() + a;
    }
}
