#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct MetaFieldHolder
{
public int Val;
}

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelStructField(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
MetaFieldHolder holder = new MetaFieldHolder { Val = inA[gid] };
int fieldExpr = (holder.Val + 9) ^ 5;
hash = (hash ^ (ulong)(fieldExpr)) * 1099511628211UL;
checksum[gid] = hash;
}
}
