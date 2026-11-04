#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct MetaPropStruct
{
public int Raw;
public int Computed => Raw + 1;
}

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelPropertyRead(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
MetaPropStruct s = new MetaPropStruct { Raw = inA[gid] };
int propExpr = (s.Computed + 11) ^ 3;
hash = (hash ^ (ulong)(propExpr)) * 1099511628211UL;
checksum[gid] = hash;
}
}
