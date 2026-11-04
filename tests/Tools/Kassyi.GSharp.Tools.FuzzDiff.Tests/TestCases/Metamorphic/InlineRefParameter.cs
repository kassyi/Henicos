#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuDeviceFunction]
public static int HelperByRef(ref int a, int b)
{
a += b;
return a;
}

[GpuKernel]
public static void KernelRefParameter(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int v0 = inA[gid];
int r = HelperByRef(ref v0, inB[gid]);
hash = (hash ^ (ulong)(r ^ v0)) * 1099511628211UL;
checksum[gid] = hash;
}
}
