using Kassyi.GSharp.Core;
using static Kassyi.GSharp.Tests.TestCases.Diagnostics.ParallelFormsHelpers;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

public static class ParallelFormsHelpers
{
    [GpuDeviceFunction]
    public static void ScaleAll(int[] data)
    {
        Gpu.GridStrideLoop(data.Length, i => data[i] *= 2);
    }

    public static int Scaled
    {
        get
        {
            Gpu.SyncGrid();
            return 0;
        }
    }

    public static int Plain => 7;
}

public struct PTid
{
    public int X;

    public PTid()
    {
        X = ThreadIndex.X;
    }
}

public struct PPlain
{
    public int X;

    public PPlain()
    {
        X = 3;
    }
}

public struct HT<T>() where T : struct
{
    public T V = new T();
}

public struct Buf
{
    public int[] Data;

    private int Scaled
    {
        get
        {
            ParallelFormsHelpers.ScaleAll(Data);
            return 0;
        }
    }

    public int Run() => Scaled;

    public int this[int i]
    {
        get
        {
            ParallelFormsHelpers.ScaleAll(Data);
            return i;
        }
    }

    public static Buf operator -(Buf b)
    {
        ParallelFormsHelpers.ScaleAll(b.Data);
        return b;
    }

    public static implicit operator int(Buf b)
    {
        ParallelFormsHelpers.ScaleAll(b.Data);
        return 0;
    }
}

public static partial class ParallelFormsKernels
{
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ExplicitNewKernel(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = new PTid().X;
        }
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void GenericNewKernel(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = new HT<PTid>().V.X;
        }
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void TargetTypedNewKernel(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            PTid t = new();
            o[0] = t.X;
        }
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void BarePropertyKernel(int[] data, int[] o)
    {
        Buf buf = new Buf { Data = data };
        if (ThreadIndex.X == 0)
        {
            o[0] = buf.Run();
        }
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UsingStaticKernel(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = Scaled;
        }
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void IndexerKernel(int[] data, int[] o)
    {
        Buf buf = new Buf { Data = data };
        if (ThreadIndex.X == 0)
        {
            o[0] = buf[1];
        }
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void UnaryOperatorKernel(int[] data, int[] o)
    {
        Buf buf = new Buf { Data = data };
        if (ThreadIndex.X == 0)
        {
            Buf negated = -buf;
            o[0] = negated.Data.Length;
        }
    }

    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ConversionKernel(int[] data, int[] o)
    {
        Buf buf = new Buf { Data = data };
        if (ThreadIndex.X == 0)
        {
            int converted = buf;
            o[0] = converted;
        }
    }

    // Correct code in the same branch: a plain getter, a construction that reaches no parallel dependency (also through
    // new T()), and nameof of a parallel-dependent property
    [GpuKernel(Cooperative = true)]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void CorrectCodeKernel(int[] o)
    {
        if (ThreadIndex.X == 0)
        {
            o[0] = Plain;
            o[1] = new HT<PPlain>().V.X;
            o[2] = nameof(ParallelFormsHelpers.Scaled).Length;
        }
    }
}
