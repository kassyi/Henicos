#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelArrayWrite(int[] arrA, int[] arrB, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
arrA[gid] = 42;
int fromB = arrB[gid] + 1;
hash = (hash ^ (ulong)(fromB)) * 1099511628211UL;
checksum[gid] = hash;
}
}
