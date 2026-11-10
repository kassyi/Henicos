#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

// Split kernels whose results are worked out by hand in FuzzSplitOracleTests. Launched on two blocks of 64 threads,
// each writes one value per thread, so the expected column is a formula of gid.
public static partial class SplitHandKernels
{
[GpuDeviceFunction]
public static int SideInc(ref int value)
{
value += 1;
return value;
}

[GpuDeviceFunction]
public static int SuspendBump(ref int value, int delta)
{
value += delta;
Gpu.SyncThreads();
return value * 3;
}

[GpuDeviceFunction]
public static int SuspendSelf(int x)
{
return Warp.ShuffleSync(0xFFFFFFFFu, x + 10, ThreadIndex.X & 31) ^ x;
}

[GpuDeviceFunction]
public static System.Collections.Generic.IEnumerable<int> CountFrom(int seed, int count)
{
for (int i = 0; i < count; i++)
{
yield return seed + i;
}
}

// C# assigns a before the shuffle reads it: (gid + 100) + (gid + 100).
[GpuKernel]
public static void AssignThenShuffleKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
int a = 0;
int r = (a = gid + 100) + Warp.ShuffleSync(0xFFFFFFFFu, a, ThreadIndex.X & 31);
checksum[gid] = (ulong)(long)r;
}

// C# reads x (3) before the right side runs; SideInc makes x 4 and returns 4: 3 + 4.
[GpuKernel]
public static void CompoundShuffleKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
int x = 3;
x += Warp.ShuffleXorSync(0xFFFFFFFFu, SideInc(ref x), 0);
checksum[gid] = (ulong)(long)x;
}

// A shuffle with delta 0 reads the calling lane: 2 * gid + gid.
[GpuKernel]
public static void UpDownSelfKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
int u = Warp.ShuffleUpSync(0xFFFFFFFFu, gid * 2, 0u) + Warp.ShuffleDownSync(0xFFFFFFFFu, gid, 0u);
checksum[gid] = (ulong)(long)u;
}

// The barrier changes no value: 5 * 3 + gid.
[GpuKernel]
public static void BarrierKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
int t = 5;
Gpu.SyncThreads();
t *= 3;
for (int k = 0; k < 2; k++)
{
t += k;
Gpu.SyncThreads();
}

t += gid - 1;
checksum[gid] = (ulong)(long)t;
}

// SuspendBump makes b 7 and returns 21; then b is read: 21 + 7. SuspendSelf(gid) is (gid + 10) ^ gid.
[GpuKernel]
public static void SuspendingCallKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
int b = 2;
int r = SuspendBump(ref b, 5) + b;
r += SuspendSelf(gid) << 8;
checksum[gid] = (ulong)(long)r;
}

// The iterator yields gid, gid + 1, gid + 2 between two barriers: 3 * gid + 3.
[GpuKernel]
public static void IteratorKernel(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
int s = 0;
Gpu.SyncThreads();
foreach (int v in CountFrom(gid, 3))
{
s += v;
}

Gpu.SyncThreads();
checksum[gid] = (ulong)(long)s;
}
}
