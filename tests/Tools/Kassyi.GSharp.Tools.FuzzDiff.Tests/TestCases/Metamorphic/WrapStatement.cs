#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaWrapKernels
{
[GpuKernel]
public static void KernelWrap(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int acc = inA[gid];
acc = (acc + inB[gid]) ^ inC[gid];
checksum[gid] = hash ^ (ulong)acc;
}
}
