#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaInlineKernels
{
[GpuDeviceFunction]
public static int MixHelper(int a, int b)
{
int step = (a ^ 0x5555AAAA) + b;
return (step * 3) ^ a;
}

[GpuKernel]
public static void KernelInline(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int v0 = inA[gid];
int v1 = inB[gid] + inC[gid];
int pureCall = MixHelper(v0 + 7, v1);
hash = (hash ^ (ulong)(pureCall ^ v0)) * 1099511628211UL;
checksum[gid] = hash;
}
}
