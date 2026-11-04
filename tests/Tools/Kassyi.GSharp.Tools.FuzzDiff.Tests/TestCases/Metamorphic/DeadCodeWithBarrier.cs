#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaDeadCodeBarrierKernels
{
[GpuKernel]
public static void KernelWithBarrier(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
ulong hash = 14695981039346656037UL;
int v0 = inA[0] + inB[0];
Gpu.SyncThreads();
hash = (hash ^ (ulong)v0) * 1099511628211UL;
if (gid < checksum.Length)
{
checksum[gid] = hash;
}
}
}
