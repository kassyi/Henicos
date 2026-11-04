#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaPipelineKernels
{
[GpuKernel]
public static void KernelPipeline(int[] inA, int[] inB, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int v = (inA[gid] ^ 0x5A) + (inB[gid] * 3);
hash = (hash ^ (ulong)v) * 1099511628211UL;
checksum[gid] = hash;
}
}
