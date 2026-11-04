#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelAssignmentOperand(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int x = inA[gid];
int sideEffect = (x += 3) + (x * 5);
hash = (hash ^ (ulong)(sideEffect)) * 1099511628211UL;
checksum[gid] = hash;
}
}
