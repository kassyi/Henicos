using Kassyi.GSharp.Core;

public struct ExplicitAndIndexedStruct
{
    public int A;
    public int B;

    public static explicit operator ExplicitAndIndexedStruct(int v) =>
        new ExplicitAndIndexedStruct { A = v * 2, B = v + 1 };

    public int this[int idx]
    {
        get => idx == 0 ? A : B;
        set
        {
            if (idx == 0) A = value;
            else B = value;
        }
    }
}

public static partial class ExplicitAndIndexedKernelHolder
{
    [GpuKernel]
    public static void Kernel(int[] xs, int[] r)
    {
        int x = xs[0];
        var s = (ExplicitAndIndexedStruct)x;
        s[1] = x + 99;
        r[0] = s[0] + s[1];
    }
}
