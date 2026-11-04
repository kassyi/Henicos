using Kassyi.GSharp.Core;

public static class GenericOuter<T> where T : unmanaged
{
    public struct InnerBox
    {
        public T Value;

        public InnerBox(T v)
        {
            Value = default(T);
            Value = v;
        }
    }
}

public static partial class GenericOuterKernelHolder
{
    [GpuKernel]
    public static void Kernel(int[] r)
    {
        var box = new GenericOuter<int>.InnerBox(42);
        r[0] = box.Value;
    }
}
