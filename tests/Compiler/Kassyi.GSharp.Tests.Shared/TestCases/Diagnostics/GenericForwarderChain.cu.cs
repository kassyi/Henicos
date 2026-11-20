using Kassyi.GSharp.Core;

// A chain of generic device functions that only pass their type parameter on: none uses it, so each is compiled once
public static partial class GenericForwarderChain
{
    [GpuDeviceFunction]
    public static int F0<T>(int x) => GenericForwarderChain.F1<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F1<T>(int x) => GenericForwarderChain.F2<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F2<T>(int x) => GenericForwarderChain.F3<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F3<T>(int x) => GenericForwarderChain.F4<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F4<T>(int x) => GenericForwarderChain.F5<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F5<T>(int x) => GenericForwarderChain.F6<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F6<T>(int x) => GenericForwarderChain.F7<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F7<T>(int x) => GenericForwarderChain.F8<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F8<T>(int x) => GenericForwarderChain.F9<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F9<T>(int x) => GenericForwarderChain.F10<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F10<T>(int x) => GenericForwarderChain.F11<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F11<T>(int x) => GenericForwarderChain.F12<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F12<T>(int x) => GenericForwarderChain.F13<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F13<T>(int x) => GenericForwarderChain.F14<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F14<T>(int x) => GenericForwarderChain.F15<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F15<T>(int x) => GenericForwarderChain.F16<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F16<T>(int x) => GenericForwarderChain.F17<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F17<T>(int x) => GenericForwarderChain.F18<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F18<T>(int x) => GenericForwarderChain.F19<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F19<T>(int x) => GenericForwarderChain.F20<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F20<T>(int x) => GenericForwarderChain.F21<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F21<T>(int x) => GenericForwarderChain.F22<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F22<T>(int x) => GenericForwarderChain.F23<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F23<T>(int x) => GenericForwarderChain.F24<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F24<T>(int x) => GenericForwarderChain.F25<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F25<T>(int x) => GenericForwarderChain.F26<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F26<T>(int x) => GenericForwarderChain.F27<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F27<T>(int x) => GenericForwarderChain.F28<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F28<T>(int x) => GenericForwarderChain.F29<T>(x) + 1;

    [GpuDeviceFunction]
    public static int F29<T>(int x) => x + 1;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ChainKernel(int[] xs)
    {
        int tid = ThreadIndex.X;
        xs[tid] = GenericForwarderChain.F0<int>(xs[tid]);
    }
}

// Each function calls the next one twice
public static partial class GenericForwarderDiamond
{
    [GpuDeviceFunction]
    public static int D0<T>(int x) => GenericForwarderDiamond.D1<T>(x) + GenericForwarderDiamond.D1<T>(x + 1);

    [GpuDeviceFunction]
    public static int D1<T>(int x) => GenericForwarderDiamond.D2<T>(x) + GenericForwarderDiamond.D2<T>(x + 1);

    [GpuDeviceFunction]
    public static int D2<T>(int x) => GenericForwarderDiamond.D3<T>(x) + GenericForwarderDiamond.D3<T>(x + 1);

    [GpuDeviceFunction]
    public static int D3<T>(int x) => GenericForwarderDiamond.D4<T>(x) + GenericForwarderDiamond.D4<T>(x + 1);

    [GpuDeviceFunction]
    public static int D4<T>(int x) => GenericForwarderDiamond.D5<T>(x) + GenericForwarderDiamond.D5<T>(x + 1);

    [GpuDeviceFunction]
    public static int D5<T>(int x) => GenericForwarderDiamond.D6<T>(x) + GenericForwarderDiamond.D6<T>(x + 1);

    [GpuDeviceFunction]
    public static int D6<T>(int x) => GenericForwarderDiamond.D7<T>(x) + GenericForwarderDiamond.D7<T>(x + 1);

    [GpuDeviceFunction]
    public static int D7<T>(int x) => GenericForwarderDiamond.D8<T>(x) + GenericForwarderDiamond.D8<T>(x + 1);

    [GpuDeviceFunction]
    public static int D8<T>(int x) => GenericForwarderDiamond.D9<T>(x) + GenericForwarderDiamond.D9<T>(x + 1);

    [GpuDeviceFunction]
    public static int D9<T>(int x) => GenericForwarderDiamond.D10<T>(x) + GenericForwarderDiamond.D10<T>(x + 1);

    [GpuDeviceFunction]
    public static int D10<T>(int x) => GenericForwarderDiamond.D11<T>(x) + GenericForwarderDiamond.D11<T>(x + 1);

    [GpuDeviceFunction]
    public static int D11<T>(int x) => GenericForwarderDiamond.D12<T>(x) + GenericForwarderDiamond.D12<T>(x + 1);

    [GpuDeviceFunction]
    public static int D12<T>(int x) => GenericForwarderDiamond.D13<T>(x) + GenericForwarderDiamond.D13<T>(x + 1);

    [GpuDeviceFunction]
    public static int D13<T>(int x) => GenericForwarderDiamond.D14<T>(x) + GenericForwarderDiamond.D14<T>(x + 1);

    [GpuDeviceFunction]
    public static int D14<T>(int x) => GenericForwarderDiamond.D15<T>(x) + GenericForwarderDiamond.D15<T>(x + 1);

    [GpuDeviceFunction]
    public static int D15<T>(int x) => GenericForwarderDiamond.D16<T>(x) + GenericForwarderDiamond.D16<T>(x + 1);

    [GpuDeviceFunction]
    public static int D16<T>(int x) => GenericForwarderDiamond.D17<T>(x) + GenericForwarderDiamond.D17<T>(x + 1);

    [GpuDeviceFunction]
    public static int D17<T>(int x) => GenericForwarderDiamond.D18<T>(x) + GenericForwarderDiamond.D18<T>(x + 1);

    [GpuDeviceFunction]
    public static int D18<T>(int x) => GenericForwarderDiamond.D19<T>(x) + GenericForwarderDiamond.D19<T>(x + 1);

    [GpuDeviceFunction]
    public static int D19<T>(int x) => x;

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DiamondKernel(int[] xs)
    {
        int tid = ThreadIndex.X;
        xs[tid] = GenericForwarderDiamond.D0<int>(xs[tid]);
    }
}

// A chain whose last function uses its type parameter: every function that passes it on is left out as well
public static partial class GenericUserChain
{
    [GpuDeviceFunction]
    public static int U0<T>(int x) => GenericUserChain.U1<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U1<T>(int x) => GenericUserChain.U2<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U2<T>(int x) => GenericUserChain.U3<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U3<T>(int x) => GenericUserChain.U4<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U4<T>(int x) => GenericUserChain.U5<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U5<T>(int x) => GenericUserChain.U6<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U6<T>(int x) => GenericUserChain.U7<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U7<T>(int x) => GenericUserChain.U8<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U8<T>(int x) => GenericUserChain.U9<T>(x) + 1;

    [GpuDeviceFunction]
    public static int U9<T>(int x)
    {
        T unused = default;
        return x;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UserKernel(int[] xs)
    {
        int tid = ThreadIndex.X;
        xs[tid] = GenericUserChain.U0<int>(xs[tid]);
    }
}

// Ping and Pong only pass T to each other; in A and B only B uses it
public static partial class GenericCycles
{
    [GpuDeviceFunction]
    public static int Ping<T>(int x) => x <= 0 ? 0 : GenericCycles.Pong<T>(x - 1);

    [GpuDeviceFunction]
    public static int Pong<T>(int x) => x <= 0 ? 1 : GenericCycles.Ping<T>(x - 1);

    [GpuDeviceFunction]
    public static int A<T>(int x) => x <= 0 ? 0 : GenericCycles.B<T>(x - 1);

    [GpuDeviceFunction]
    public static int B<T>(int x)
    {
        T unused = default;
        return x <= 0 ? 1 : GenericCycles.A<T>(x - 1);
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CycleKernel(int[] xs)
    {
        int tid = ThreadIndex.X;
        xs[tid] = GenericCycles.Ping<int>(xs[tid]) + GenericCycles.A<int>(xs[tid]);
    }
}
