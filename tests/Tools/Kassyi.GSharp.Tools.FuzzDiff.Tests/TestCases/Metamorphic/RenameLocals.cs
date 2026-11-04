#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public struct MetaPair
{
public int First;
public int Second;
}

public static partial class MetaRenameKernels
{
[GpuKernel]
public static void KernelRename(int[] inA, int[] inB, int[] inC, ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
int alpha = inA[gid] + inB[gid];
int beta = alpha ^ inC[gid];
MetaPair pair = new MetaPair { First = alpha, Second = beta };
int combined = pair.First + pair.Second;
hash = (hash ^ (ulong)(combined)) * 1099511628211UL;
checksum[gid] = hash;
}
}
