#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaConstantKernels
{
[GpuKernel]
public static void KernelConstantShort(int[] inA, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
short s = (1 << 2) | 1;
checksum[gid] = hash ^ (ulong)s;
}
}
