#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaConstantKernels
{
[GpuKernel]
public static void KernelConstantByte(int[] inA, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
byte b = (1 + 2) * 3;
checksum[gid] = hash ^ (ulong)b;
}
}
