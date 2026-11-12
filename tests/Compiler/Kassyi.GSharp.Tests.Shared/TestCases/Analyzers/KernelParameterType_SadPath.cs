using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public sealed class KernelParameterBag
{
    public int Count;
}

public interface IKernelParameterShape
{
}

public delegate int KernelParameterTransform(int x);

public struct KernelParameterPoint
{
    public int X;
}

public static partial class KernelParameterTypeCases
{
    [GpuKernel]
    public static void ClassParameter(KernelParameterBag bag, int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void InterfaceParameter(IKernelParameterShape shape, int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void DelegateParameter(KernelParameterTransform transform, int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void ObjectByReference(ref object value, int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void ExtensionReceiver(this KernelParameterBag bag, int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void StringParameter(string text, int[] o) { o[0] = 42; }

    [GpuKernel]
    public static void ArrayOfClass(KernelParameterBag[] bags, int[] o) { o[0] = 42; }

    // Arrays, spans, values and structs have a device representation: no GSH0112
    [GpuKernel]
    public static void DeviceParameters(int[] o, Span<float> s, KernelParameterPoint p, KernelParameterPoint[] ps,
        ref int r)
    {
        o[0] = p.X + ps[0].X + r + (int)s[0];
    }
}
