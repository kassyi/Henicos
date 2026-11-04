#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelArrayRead(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int arrExpr = (inA[gid] + inB[gid]) ^ 9;
hash = (hash ^ (ulong)(arrExpr)) * 1099511628211UL;
checksum[gid] = hash;
}
}
