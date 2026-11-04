#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelBroken(int[] inA, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
UnknownMysteryType broken = inA[gid];
checksum[gid] = (ulong)broken;
}
}
