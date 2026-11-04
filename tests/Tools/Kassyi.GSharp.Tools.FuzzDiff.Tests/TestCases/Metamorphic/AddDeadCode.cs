#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaDeadCodeKernels
{
[GpuKernel]
public static void KernelDeadCode(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int v0 = (inA[gid] + inB[gid]) ^ inC[gid];
hash = (hash ^ (ulong)(v0)) * 1099511628211UL;
checksum[gid] = hash;
}
}
