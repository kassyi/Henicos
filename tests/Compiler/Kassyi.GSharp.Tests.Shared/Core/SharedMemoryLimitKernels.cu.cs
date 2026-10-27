// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Shared memory sizes on the CPU paths (#544): a dynamic declaration (count not a C# constant) is placed in the
/// <c>sharedMemBytes</c> of the launch with the CUDA rules and a launch that does not give it enough throws; a static
/// declaration (a constant count) is not counted, and neither has a fixed limit of bytes or declarations.
/// </summary>
public static partial class SharedMemoryLimitKernels
{
    // One dynamic declaration of n ints, written by the threads of the block in turn; a barrier makes it a state machine,
    // whose context holds the declaration
    [GpuKernel]
    public static void FillKernel(int[] output, int n)
    {
        var s = new SharedMemory<int>(n);
        for (int i = ThreadIndex.X; i < n; i += BlockDimension.X)
        {
            s[i] = i + 1;
        }

        Gpu.SyncThreads();
        for (int i = ThreadIndex.X; i < n; i += BlockDimension.X)
        {
            output[i] = s[(i + 1) % n];
        }
    }

    // The same without a barrier: a plain kernel function, whose declaration stays a local
    [GpuKernel]
    public static void FillWithoutBarrierKernel(int[] output, int n)
    {
        var s = new SharedMemory<int>(n);
        for (int i = ThreadIndex.X; i < n; i += BlockDimension.X)
        {
            s[i] = 2 * i;
            output[i] = s[i];
        }
    }

    // Two dynamic declarations of different element types: CUDA aligns each region to 16 bytes, so a ints and b doubles
    // need align16(4a) + 8b bytes
    [GpuKernel]
    public static void TwoRegionsKernel(int[] output, int a, int b)
    {
        int tid = ThreadIndex.X;
        var first = new SharedMemory<int>(a);
        var second = new SharedMemory<double>(b);
        if (tid < a)
        {
            first[tid] = tid + 1;
        }

        if (tid < b)
        {
            second[tid] = tid + 0.5;
        }

        Gpu.SyncThreads();
        output[tid] = (tid < a ? first[tid] : 0) + (tid < b ? (int)(second[tid] * 2) : 0);
    }

    // The count is a local that the state machine keeps in its context (it is read after the barrier): the redeclaration at
    // the start of each managed resume must not evaluate it
    [GpuKernel]
    public static void HoistedCountKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int n = 4 + 4 * (BlockIndex.X % 2);
        var a = new SharedMemory<int>(n);
        if (tid < n)
        {
            a[tid] = tid + 1;
        }

        Gpu.SyncThreads();
        output[BlockIndex.X * BlockDimension.X + tid] = tid < n ? a[(tid + 1) % n] : 0;
    }

    // A device function that places its region first, then the kernel's region after it: 5 ints at 0, then 3 doubles at 32
    // (56 bytes). Placed the other way round (the kernel's region first), they would need only 52 bytes
    [GpuDeviceFunction]
    public static int FirstRegion(int count, int tid)
    {
        var s = new SharedMemory<int>(count);
        if (tid < count)
        {
            s[tid] = tid + 1;
        }

        Gpu.SyncThreads();
        return tid < count ? s[tid] : 0;
    }

    [GpuKernel]
    public static void RegionOrderKernel(int[] output, int a, int b)
    {
        int tid = ThreadIndex.X;
        int first = FirstRegion(a, tid);
        var second = new SharedMemory<double>(b);
        if (tid < b)
        {
            second[tid] = tid + 0.5;
        }

        Gpu.SyncThreads();
        output[tid] = first + (tid < b ? (int)(second[tid] * 2) : 0);
    }

    // 70 static declarations (constant counts) and one dynamic one: only the dynamic one is counted in sharedMemBytes,
    // and there is no limit on the number of declarations. Every thread writes the same value into each static buffer
    // and reads it back, so no barrier is needed
    [GpuKernel]
    public static void ManyStaticDeclarationsKernel(int[] output, int n)
    {
        int tid = ThreadIndex.X;
        var s00 = new SharedMemory<int>(4);
        s00[tid % 4] = 0;
        var s01 = new SharedMemory<int>(4);
        s01[tid % 4] = 1;
        var s02 = new SharedMemory<int>(4);
        s02[tid % 4] = 2;
        var s03 = new SharedMemory<int>(4);
        s03[tid % 4] = 3;
        var s04 = new SharedMemory<int>(4);
        s04[tid % 4] = 4;
        var s05 = new SharedMemory<int>(4);
        s05[tid % 4] = 5;
        var s06 = new SharedMemory<int>(4);
        s06[tid % 4] = 6;
        var s07 = new SharedMemory<int>(4);
        s07[tid % 4] = 7;
        var s08 = new SharedMemory<int>(4);
        s08[tid % 4] = 8;
        var s09 = new SharedMemory<int>(4);
        s09[tid % 4] = 9;
        var s10 = new SharedMemory<int>(4);
        s10[tid % 4] = 10;
        var s11 = new SharedMemory<int>(4);
        s11[tid % 4] = 11;
        var s12 = new SharedMemory<int>(4);
        s12[tid % 4] = 12;
        var s13 = new SharedMemory<int>(4);
        s13[tid % 4] = 13;
        var s14 = new SharedMemory<int>(4);
        s14[tid % 4] = 14;
        var s15 = new SharedMemory<int>(4);
        s15[tid % 4] = 15;
        var s16 = new SharedMemory<int>(4);
        s16[tid % 4] = 16;
        var s17 = new SharedMemory<int>(4);
        s17[tid % 4] = 17;
        var s18 = new SharedMemory<int>(4);
        s18[tid % 4] = 18;
        var s19 = new SharedMemory<int>(4);
        s19[tid % 4] = 19;
        var s20 = new SharedMemory<int>(4);
        s20[tid % 4] = 20;
        var s21 = new SharedMemory<int>(4);
        s21[tid % 4] = 21;
        var s22 = new SharedMemory<int>(4);
        s22[tid % 4] = 22;
        var s23 = new SharedMemory<int>(4);
        s23[tid % 4] = 23;
        var s24 = new SharedMemory<int>(4);
        s24[tid % 4] = 24;
        var s25 = new SharedMemory<int>(4);
        s25[tid % 4] = 25;
        var s26 = new SharedMemory<int>(4);
        s26[tid % 4] = 26;
        var s27 = new SharedMemory<int>(4);
        s27[tid % 4] = 27;
        var s28 = new SharedMemory<int>(4);
        s28[tid % 4] = 28;
        var s29 = new SharedMemory<int>(4);
        s29[tid % 4] = 29;
        var s30 = new SharedMemory<int>(4);
        s30[tid % 4] = 30;
        var s31 = new SharedMemory<int>(4);
        s31[tid % 4] = 31;
        var s32 = new SharedMemory<int>(4);
        s32[tid % 4] = 32;
        var s33 = new SharedMemory<int>(4);
        s33[tid % 4] = 33;
        var s34 = new SharedMemory<int>(4);
        s34[tid % 4] = 34;
        var s35 = new SharedMemory<int>(4);
        s35[tid % 4] = 35;
        var s36 = new SharedMemory<int>(4);
        s36[tid % 4] = 36;
        var s37 = new SharedMemory<int>(4);
        s37[tid % 4] = 37;
        var s38 = new SharedMemory<int>(4);
        s38[tid % 4] = 38;
        var s39 = new SharedMemory<int>(4);
        s39[tid % 4] = 39;
        var s40 = new SharedMemory<int>(4);
        s40[tid % 4] = 40;
        var s41 = new SharedMemory<int>(4);
        s41[tid % 4] = 41;
        var s42 = new SharedMemory<int>(4);
        s42[tid % 4] = 42;
        var s43 = new SharedMemory<int>(4);
        s43[tid % 4] = 43;
        var s44 = new SharedMemory<int>(4);
        s44[tid % 4] = 44;
        var s45 = new SharedMemory<int>(4);
        s45[tid % 4] = 45;
        var s46 = new SharedMemory<int>(4);
        s46[tid % 4] = 46;
        var s47 = new SharedMemory<int>(4);
        s47[tid % 4] = 47;
        var s48 = new SharedMemory<int>(4);
        s48[tid % 4] = 48;
        var s49 = new SharedMemory<int>(4);
        s49[tid % 4] = 49;
        var s50 = new SharedMemory<int>(4);
        s50[tid % 4] = 50;
        var s51 = new SharedMemory<int>(4);
        s51[tid % 4] = 51;
        var s52 = new SharedMemory<int>(4);
        s52[tid % 4] = 52;
        var s53 = new SharedMemory<int>(4);
        s53[tid % 4] = 53;
        var s54 = new SharedMemory<int>(4);
        s54[tid % 4] = 54;
        var s55 = new SharedMemory<int>(4);
        s55[tid % 4] = 55;
        var s56 = new SharedMemory<int>(4);
        s56[tid % 4] = 56;
        var s57 = new SharedMemory<int>(4);
        s57[tid % 4] = 57;
        var s58 = new SharedMemory<int>(4);
        s58[tid % 4] = 58;
        var s59 = new SharedMemory<int>(4);
        s59[tid % 4] = 59;
        var s60 = new SharedMemory<int>(4);
        s60[tid % 4] = 60;
        var s61 = new SharedMemory<int>(4);
        s61[tid % 4] = 61;
        var s62 = new SharedMemory<int>(4);
        s62[tid % 4] = 62;
        var s63 = new SharedMemory<int>(4);
        s63[tid % 4] = 63;
        var s64 = new SharedMemory<int>(4);
        s64[tid % 4] = 64;
        var s65 = new SharedMemory<int>(4);
        s65[tid % 4] = 65;
        var s66 = new SharedMemory<int>(4);
        s66[tid % 4] = 66;
        var s67 = new SharedMemory<int>(4);
        s67[tid % 4] = 67;
        var s68 = new SharedMemory<int>(4);
        s68[tid % 4] = 68;
        var s69 = new SharedMemory<int>(4);
        s69[tid % 4] = 69;
        var d = new SharedMemory<int>(n);
        d[tid] = tid;
        output[tid] = d[tid] + s00[tid % 4]+s01[tid % 4]+s02[tid % 4]+s03[tid % 4]+s04[tid % 4]+s05[tid % 4]+s06[tid % 4]+s07[tid % 4]+s08[tid % 4]+s09[tid % 4]+s10[tid % 4]+s11[tid % 4]+s12[tid % 4]+s13[tid % 4]+s14[tid % 4]+s15[tid % 4]+s16[tid % 4]+s17[tid % 4]+s18[tid % 4]+s19[tid % 4]+s20[tid % 4]+s21[tid % 4]+s22[tid % 4]+s23[tid % 4]+s24[tid % 4]+s25[tid % 4]+s26[tid % 4]+s27[tid % 4]+s28[tid % 4]+s29[tid % 4]+s30[tid % 4]+s31[tid % 4]+s32[tid % 4]+s33[tid % 4]+s34[tid % 4]+s35[tid % 4]+s36[tid % 4]+s37[tid % 4]+s38[tid % 4]+s39[tid % 4]+s40[tid % 4]+s41[tid % 4]+s42[tid % 4]+s43[tid % 4]+s44[tid % 4]+s45[tid % 4]+s46[tid % 4]+s47[tid % 4]+s48[tid % 4]+s49[tid % 4]+s50[tid % 4]+s51[tid % 4]+s52[tid % 4]+s53[tid % 4]+s54[tid % 4]+s55[tid % 4]+s56[tid % 4]+s57[tid % 4]+s58[tid % 4]+s59[tid % 4]+s60[tid % 4]+s61[tid % 4]+s62[tid % 4]+s63[tid % 4]+s64[tid % 4]+s65[tid % 4]+s66[tid % 4]+s67[tid % 4]+s68[tid % 4]+s69[tid % 4];
    }
}
