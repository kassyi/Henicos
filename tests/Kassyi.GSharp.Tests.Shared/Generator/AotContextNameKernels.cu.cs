using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Generator.AotContextName;

public struct Pair
{
    public int A;
}

public struct Box<T> where T : unmanaged
{
    public T Value;
}

public static partial class AotContextNameKernels
{
    [GpuKernel]
    public static void First(int[] data, Pair pair)
    {
        data[ThreadIndex.X] = pair.A;
    }

    [GpuKernel]
    public static void Second(int[] data, Pair pair)
    {
        data[ThreadIndex.X] = pair.A + 1;
    }

    [GpuKernel]
    public static void Third(int[] data, Box<int> box)
    {
        data[ThreadIndex.X] = box.Value;
    }
}
