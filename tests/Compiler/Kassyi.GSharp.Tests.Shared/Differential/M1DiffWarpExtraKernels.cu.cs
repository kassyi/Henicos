// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Generic struct with two type parameters whose constructor accesses a static <see cref="SharedMemory{T}"/> buffer per
/// closed <c>(TFirst, TSecond)</c> instantiation (#543).
/// </summary>
public struct SharedPairSlot<TFirst, TSecond>
    where TFirst : unmanaged
    where TSecond : unmanaged
{
    public int Value;

    public SharedPairSlot(bool write, int lane, int inputValue)
    {
        var smem = new SharedMemory<int>(32);
        if (write)
        {
            smem[lane] = inputValue;
            Value = 0;
        }
        else
        {
            Value = smem[lane];
        }
    }
}

/// <summary>
/// Additional differential kernels for sub-warp upper-half conditional shuffle (#1001), multi-type-parameter generic
/// struct static <see cref="SharedMemory{T}"/> (#543), and <see cref="Gpu.MatchAnySync(uint, int)"/> /
/// <see cref="Gpu.MatchAllSync(uint, int, ref int)"/> (#1002).
/// </summary>
[SkipSnapshotConsistency("Differential test kernels for #1001, #543, #1002")]
public static partial class M1DiffWarpExtraKernels
{
    /// <summary>The columns of <see cref="SubWarpUpperHalfConditionalShuffleKernel"/>.</summary>
    public static readonly string[] UpperHalfConditionalShuffleColumns =
    [
        "upperHalfUp1",
        "reconvergedFromUpperHalfDown16",
        "reconvergedFullWarpXor16",
    ];

    /// <summary>The columns of <see cref="GenericPairStructStaticSharedMemoryKernel"/>.</summary>
    public static readonly string[] GenericPairStructSharedColumns =
    [
        "SharedPairSlot<int,int>",
        "SharedPairSlot<int,uint>",
        "SharedPairSlot<uint,int>",
    ];

    /// <summary>The columns of <see cref="WarpMatchAnyAndAllSyncKernel"/>.</summary>
    public static readonly string[] WarpMatchColumns =
    [
        "MatchAnySync(int,group4)",
        "MatchAnySync(uint,bucket8)",
        "MatchAnySync(long,parity)",
        "MatchAnySync(ulong,halfMask)",
        "MatchAllSync(int,uniform).mask",
        "MatchAllSync(int,uniform).pred",
        "MatchAllSync(uint,divergent).mask",
        "MatchAllSync(uint,divergent).pred",
        "MatchAllSync(long,halfUniform).mask",
        "MatchAllSync(long,halfUniform).pred",
        "MatchAllSync(ulong,halfDivergent).mask",
        "MatchAllSync(ulong,halfDivergent).pred",
    ];

    // 1. Sub-warp conditional shuffle in the upper half (if (lane >= 16)) followed by reconverged full-warp shuffles
    //    that read the updated values from the upper-half lanes (#1001).
    [GpuKernel]
    public static void SubWarpUpperHalfConditionalShuffleKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        int lane = ThreadIndex.X & 31;
        int n = input.Length;

        int baseVal = tid < n ? (input[tid] & 0xFF) + 1 : 1;
        int a = baseVal * 10 + lane;
        int b = baseVal * 1000 + lane;

        if (lane >= 16)
        {
            uint highHalfMask = 0xFFFF0000u;
            a = Warp.ShuffleUpSync(highHalfMask, a, 1, 16);
        }

        uint fullMask = 0xFFFFFFFFu;
        int fromUpper = Warp.ShuffleDownSync(fullMask, a, 16);
        int xor16 = Warp.ShuffleXorSync(fullMask, b, 16);

        if (tid < n)
        {
            int o = tid * 3;
            output[o + 0] = a;
            output[o + 1] = fromUpper;
            output[o + 2] = xor16;
        }
    }

    // 2. Distinct multi-parameter generic struct instantiations declaring static SharedMemory<int>(32) (#543).
    [GpuKernel]
    public static void GenericPairStructStaticSharedMemoryKernel(int[] input, int[] output)
    {
        int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        int lane = ThreadIndex.X & 31;
        int n = input.Length;

        int v = tid < n ? (input[tid] & 0xFF) + 1 : 1;
        int valA = v * 10 + lane;
        int valB = v * 100 + lane;
        int valC = v * 1000 + lane;

        var writeA = new SharedPairSlot<int, int>(true, lane, valA);
        var writeB = new SharedPairSlot<int, uint>(true, lane, valB);
        var writeC = new SharedPairSlot<uint, int>(true, lane, valC);
        int writeZeros = writeA.Value + writeB.Value + writeC.Value;

        Gpu.SyncThreads();

        int mirrorLane = 31 - lane;
        var readA = new SharedPairSlot<int, int>(false, mirrorLane, 0);
        var readB = new SharedPairSlot<int, uint>(false, mirrorLane, 0);
        var readC = new SharedPairSlot<uint, int>(false, mirrorLane, 0);

        if (tid < n)
        {
            int o = tid * 3;
            output[o + 0] = readA.Value + writeZeros;
            output[o + 1] = readB.Value + writeZeros;
            output[o + 2] = readC.Value + writeZeros;
        }
    }

    // 3. Gpu.MatchAnySync and Gpu.MatchAllSync across int, uint, long, ulong overloads with full-warp and sub-warp
    //    masks (#1002).
    [GpuKernel]
    public static void WarpMatchAnyAndAllSyncKernel(int[] input, uint[] output)
    {
        int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        int lane = ThreadIndex.X & 31;
        int n = input.Length;
        uint fullMask = 0xFFFFFFFFu;

        int raw = tid < n ? input[tid] : 0;
        int group4Key = raw & 3;
        uint bucket8Key = (uint)(lane >> 3) + (uint)((raw & 1) << 4);
        long parityKey = ((long)(lane & 1) << 40) | (long)(raw & 1);
        ulong halfKey = 0x100000000UL + (ulong)(lane & 3);

        uint anyGroup4 = Gpu.MatchAnySync(fullMask, group4Key);
        uint anyBucket8 = Gpu.MatchAnySync(fullMask, bucket8Key);
        uint anyParity = Gpu.MatchAnySync(fullMask, parityKey);

        uint anyHalf;
        if (lane < 16)
        {
            anyHalf = Gpu.MatchAnySync(0x0000FFFFu, halfKey);
        }
        else
        {
            anyHalf = Gpu.MatchAnySync(0xFFFF0000u, halfKey);
        }

        int uniformInt = (Warp.ShuffleSync(fullMask, raw, 0) & 0xFF) + 7;
        int predUniform = -1;
        uint allUniformMask = Gpu.MatchAllSync(fullMask, uniformInt, ref predUniform);

        uint divergentUint = (uint)uniformInt + (lane == 31 ? 1u : 0u);
        int predDivergent = -1;
        uint allDivergentMask = Gpu.MatchAllSync(fullMask, divergentUint, ref predDivergent);

        long halfUniformLong = (lane < 16 ? 0x123456789ABCDEF0L : 0x0FEDCBA987654321L) + (uniformInt & 15);
        int predHalfUniform = -1;
        uint allHalfUniformMask;
        if (lane < 16)
        {
            allHalfUniformMask = Gpu.MatchAllSync(0x0000FFFFu, halfUniformLong, ref predHalfUniform);
        }
        else
        {
            allHalfUniformMask = Gpu.MatchAllSync(0xFFFF0000u, halfUniformLong, ref predHalfUniform);
        }

        ulong halfDivergentUlong = 0xCAFEBABE00000000UL + (ulong)uniformInt + (lane == 20 ? 1UL : 0UL);
        int predHalfDivergent = -1;
        uint allHalfDivergentMask;
        if (lane < 16)
        {
            allHalfDivergentMask = Gpu.MatchAllSync(0x0000FFFFu, halfDivergentUlong, ref predHalfDivergent);
        }
        else
        {
            allHalfDivergentMask = Gpu.MatchAllSync(0xFFFF0000u, halfDivergentUlong, ref predHalfDivergent);
        }

        if (tid < n)
        {
            int o = tid * 12;
            output[o + 0] = anyGroup4;
            output[o + 1] = anyBucket8;
            output[o + 2] = anyParity;
            output[o + 3] = anyHalf;
            output[o + 4] = allUniformMask;
            output[o + 5] = (uint)predUniform;
            output[o + 6] = allDivergentMask;
            output[o + 7] = (uint)predDivergent;
            output[o + 8] = allHalfUniformMask;
            output[o + 9] = (uint)predHalfUniform;
            output[o + 10] = allHalfDivergentMask;
            output[o + 11] = (uint)predHalfDivergent;
        }
    }
}
