#nullable enable
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Tests.DeviceLibrary;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels exercising cross-assembly <c>[GpuDeviceExport]</c> structs, explicit/packed layouts,
/// <c>[GpuDeviceFunction]</c> chains, <c>[GpuOperatorMacro]</c> expansions (#711), and barrier-suspending library calls (#433).
/// </summary>
public static partial class CrossAssemblyDifferentialKernels
{
    public static readonly string[] FunctionsAndMacrosColumns =
    [
        "mu_unqualified",
        "mu_qualified",
        "sum_pair",
        "ext_weighted_diff",
        "twice_internal_helper",
        "overload_int",
        "overload_long",
        "macro_add_pair",
        "macro_add_calls",
        "macro_private_diff",
    ];

    [GpuKernel]
    public static void CrossAssemblyVec2Kernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int o = tid * 3;

        var v2 = new Vec2(x, y);
        r[o + 0] = (int)v2.X;
        r[o + 1] = (int)v2.Y;
        r[o + 2] = (int)v2.Sum;
    }

    [GpuKernel]
    public static void CrossAssemblyParticleKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        var particle = new Particle(new Float3((float)x, (float)y, 2f), 3f);
        r[tid] = (int)particle.Weight;
    }

    [GpuKernel]
    public static void CrossAssemblyScaledKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        var scaled = new Scaled(x);
        r[tid] = scaled.V;
    }

    [GpuKernel]
    public static void CrossAssemblyTransitiveStructKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        int o = tid * 2;
        var alpha = new Alpha(x);
        r[o + 0] = alpha.Inner.V;
        r[o + 1] = alpha.Probe;
    }

    [GpuKernel]
    public static void CrossAssemblyFloat3FunctionsKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int o = tid * 2;
        float len3 = LibMath.Length3((float)x, (float)y, 2f);
        float dot3 = LibMath.Dot3(new Float3((float)x, (float)y, 2f));
        r[o + 0] = (int)len3;
        r[o + 1] = (int)dot3;
    }

    [GpuKernel]
    public static void CrossAssemblyExplicitPackedLayoutKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int o = tid * 2;
        var swap = new SwapPair { A = x + 10, B = y + 20 };
        var packed = new PackedPair { Tag = (byte)(x & 0x7F), Value = y + 30 };
        r[o + 0] = (swap.A * 3) + swap.B;
        r[o + 1] = packed.Tag + packed.Value;
    }

    [GpuKernel]
    public static void CrossAssemblyDisposableGuardKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        using (var guard = new LibDisposableGuard { Log = r, Slot = tid, Marker = x + y })
        {
        }
    }

    [GpuKernel]
    public static void CrossAssemblyFunctionsAndMacrosKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length)
        {
            return;
        }

        int x = xs[tid];
        int y = ys[tid];
        uint odd = ((uint)x * 2u) | 1u;
        int o = tid * 10;

        uint mu1 = LibMath.Mu(odd);
        uint mu2 = Kassyi.GSharp.Tests.DeviceLibrary.LibMath.Mu(odd);

        var pair = new Pair { A = x, B = y };
        int sumPair = LibMath.SumPair(pair);
        int extDiff = pair.WeightedDifference(3);
        int twice = LibMath.Twice(x);

        int ovInt = LibLanguageExports.CombineOverload(x, y);
        long ovLong = LibLanguageExports.CombineOverload((long)x, (long)y);

        LibCounter counter = default;
        int addPair = LibMath.AddPair(ref counter, pair);
        int diffPair = LibMath.Difference(pair);

        r[o + 0] = (int)mu1;
        r[o + 1] = (int)mu2;
        r[o + 2] = sumPair;
        r[o + 3] = extDiff;
        r[o + 4] = twice;
        r[o + 5] = ovInt;
        r[o + 6] = (int)ovLong;
        r[o + 7] = addPair;
        r[o + 8] = counter.Calls;
        r[o + 9] = diffPair;
    }

    [GpuKernel]
    public static void CrossAssemblyExplicitLayoutArrayKernel(
        SwapPair[] swaps,
        PackedPair[] packeds,
        Sized[] sizeds,
        IntFloatUnion[] unions,
        int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= swaps.Length)
        {
            return;
        }

        int o = tid * 4;
        r[o + 0] = (swaps[tid].A * 3) + swaps[tid].B;
        r[o + 1] = packeds[tid].Tag + packeds[tid].Value;
        r[o + 2] = sizeds[tid].A;
        r[o + 3] = unions[tid].Bits ^ unions[tid].Tag;

        swaps[tid].A += 5;
        swaps[tid].B -= 3;
        sizeds[tid].A = (sizeds[tid].A * 2) + 1;
        unions[tid].Value = -unions[tid].Value;
        unions[tid].Tag += 9;
    }

    [GpuKernel]
    public static void CrossAssemblyBarrierExchangeKernel(int[] inputs, int[] scratch, int[] r)
    {
        int localTid = ThreadIndex.X;
        int globalTid = BlockIndex.X * BlockDimension.X + localTid;
        if (globalTid >= inputs.Length)
        {
            return;
        }

        int exchanged = LibMath.Exchange(scratch, localTid, inputs[globalTid]);
        r[globalTid] = exchanged + inputs[globalTid];
    }
}
