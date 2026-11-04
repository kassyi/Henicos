#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuDeviceFunction]
public static int HelperWithBarrier(int a)
{
Gpu.SyncThreads();
return a + 1;
}

[GpuKernel]
public static void KernelBarrierHelper(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int r = HelperWithBarrier(inA[gid]);
hash = (hash ^ (ulong)(r)) * 1099511628211UL;
checksum[gid] = hash;
}
}
