#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Marshaling;

public enum Issue1264Mode
{
    A = 0,
    B = 1,
}

public enum Issue1264ByteMode : byte
{
    A = 0,
    B = 1,
}

public enum Issue1264LongMode : long
{
    A = 0,
    B = 1,
}

public struct Issue1264Job
{
    public int[] Data;
    public Issue1264Mode M;
}

public struct Issue1264ByteJob
{
    public int[] Data;
    public Issue1264ByteMode M;
}

public struct Issue1264LongJob
{
    public int[] Data;
    public Issue1264LongMode M;
}

[SkipSnapshotConsistency("Issue #1264")]
public static partial class EnumFieldMarshalingKernels
{
    [GpuKernel]
    public static void RunJob(Issue1264Job job)
    {
        int tid = ThreadIndex.X;
        if (tid < job.Data.Length)
        {
            job.Data[tid] = job.M == Issue1264Mode.B ? 42 : 7;
        }
    }

    [GpuKernel]
    public static void RunByteJob(Issue1264ByteJob job)
    {
        int tid = ThreadIndex.X;
        if (tid < job.Data.Length)
        {
            job.Data[tid] = job.M == Issue1264ByteMode.B ? 42 : 7;
        }
    }

    [GpuKernel]
    public static void RunLongJob(Issue1264LongJob job)
    {
        int tid = ThreadIndex.X;
        if (tid < job.Data.Length)
        {
            job.Data[tid] = job.M == Issue1264LongMode.B ? 42 : 7;
        }
    }
}
