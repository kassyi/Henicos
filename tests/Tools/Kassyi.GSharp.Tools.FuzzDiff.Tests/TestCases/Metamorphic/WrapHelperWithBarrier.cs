#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class MetaRejectKernels
{
[GpuDeviceFunction]
public static void HelperSync()
{
Gpu.SyncThreads();
}

[GpuKernel]
public static void KernelHelperWithBarrier(ulong[] checksum)
{
int gid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
if (gid >= checksum.Length)
{
return;
}

ulong hash = 14695981039346656037UL;
HelperSync();
checksum[gid] = hash;
}
}
