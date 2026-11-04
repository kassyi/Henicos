#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelDependentPair(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int a = inA[gid] + 11;
int dependent = (a * 5) + 3;
a = dependent + inC[gid];
hash = (hash ^ (ulong)(a ^ dependent)) * 1099511628211UL;
checksum[gid] = hash;
}
}
