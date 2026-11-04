#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelFloatingPoint(float[] fInA, float[] fInB, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
float fA = fInA[gid];
float fB = fInB[gid];
float fSum = fA + fB;
float fProd = fA * fB;
double dSum = (double)fA + (double)fB;
hash = (hash ^ (ulong)(BitConverter.SingleToUInt32Bits(fSum) ^ BitConverter.SingleToUInt32Bits(fProd) ^ (uint)dSum)) * 1099511628211UL;
checksum[gid] = hash;
}
}
