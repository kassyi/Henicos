using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct GSH_57_ReservedFieldStruct
{
    public int __gsharp_value;
}

public static partial class GSH_57_DoubleUnderscoreIdentifier_SadPath
{
    [GpuKernel]
    public static void LocalKernel(int[] data)
    {
        int __gsharp_state = data[ThreadIndex.X];
        data[ThreadIndex.X] = __gsharp_state;
    }

    [GpuKernel]
    public static void ParameterKernel(int[] a__b)
    {
        a__b[ThreadIndex.X] = 0;
    }

    [GpuKernel]
    public static void PatternKernel(int[] data)
    {
        if (data[ThreadIndex.X] is int __tmp)
        {
            data[ThreadIndex.X] = __tmp;
        }
    }

    [GpuDeviceFunction]
    public static int __Helper(int x) => x;

    [GpuKernel]
    public static void StructFieldKernel(GSH_57_ReservedFieldStruct[] items)
    {
        items[ThreadIndex.X].__gsharp_value = 1;
    }
}
