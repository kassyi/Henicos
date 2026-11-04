#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelDivision(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int x = inA[gid];
int y = inB[gid];
int divExpr = (x / 3) ^ (y % 5);
hash = (hash ^ (ulong)(divExpr)) * 1099511628211UL;
checksum[gid] = hash;
}
}
