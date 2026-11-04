#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaCommutativeKernels
{
[GpuKernel]
public static void KernelCommutative(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int x = inA[gid];
int y = inB[gid];
int sum = (x - 19) + (y << 2);
hash = (hash ^ (ulong)(sum ^ inC[gid])) * 1099511628211UL;
checksum[gid] = hash;
}
}
