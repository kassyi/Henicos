#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuKernel]
public static void KernelGuardedArms(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int x = inA[gid];
int y = inB[gid];
bool flag = true;
int guarded = flag ? (x + 99) : (y * 3);
bool shortCircuit = flag && ((x + 5) > 0);
hash = (hash ^ (ulong)(guarded ^ (shortCircuit ? 1 : 0))) * 1099511628211UL;
checksum[gid] = hash;
}
}
