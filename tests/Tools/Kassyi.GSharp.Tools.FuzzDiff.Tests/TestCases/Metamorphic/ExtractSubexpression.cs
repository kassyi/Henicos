#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaExtractKernels
{
[GpuKernel]
public static void KernelExtract(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int x = inA[gid];
int y = inB[gid];
int z = ((x ^ 0x13579BDF) + (y * 3)) ^ inC[gid];
int safeDiv = (y != 0 && y != int.MinValue) ? (x / y) : 0;
hash = (hash ^ (ulong)(z ^ safeDiv)) * 1099511628211UL;
checksum[gid] = hash;
}
}
