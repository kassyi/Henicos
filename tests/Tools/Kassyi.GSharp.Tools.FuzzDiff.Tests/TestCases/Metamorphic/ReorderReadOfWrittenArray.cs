#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelReadOfWrittenArray(int[] arrA, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
arrA[gid] = 42;
int fromA = arrA[gid] + 1;
int other = inC[gid] ^ 7;
hash = (hash ^ (ulong)(fromA ^ other)) * 1099511628211UL;
checksum[gid] = hash;
}
}
