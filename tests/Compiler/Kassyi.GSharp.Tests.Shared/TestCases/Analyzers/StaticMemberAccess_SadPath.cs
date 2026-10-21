using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static class Cfg
{
    public static readonly int M = 3;
    public static int Mutable = 5;
    public static int Prop => 7;
}

public static partial class K
{
    public static int SameClassStaticField = 10;
    public static int SameClassStaticProp => 20;

    [GpuKernel]
    public static void KernelWithStaticAccess(int[] a)
    {
        a[ThreadIndex.X] = Cfg.M + Cfg.Mutable + Cfg.Prop + SameClassStaticField + SameClassStaticProp;
    }

    [GpuDeviceFunction]
    public static int DeviceFuncWithStaticAccess()
    {
        return Cfg.M;
    }
}
