using Kassyi.GSharp.Core;

public static partial class UserInlinePtxCase
{
    [GpuDeviceFunction]
    public static void InlinePtx(ref int x)
    {
        x += 42;
    }

    [GpuKernel]
    public static void Kernel(int[] r)
    {
        int v = 10;
        UserInlinePtxCase.InlinePtx(ref v);
        r[0] = v;
    }
}
