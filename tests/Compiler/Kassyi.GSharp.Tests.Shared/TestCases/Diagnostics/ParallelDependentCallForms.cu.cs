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
}

public struct PTid
{
    public int X;

    public PTid()
    {
        X = ThreadIndex.X;
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
    public static void FormsKernel(int[] data, int[] o)
    {
        Buf buf = new Buf { Data = data };
        if (ThreadIndex.X == 0)
        {
            o[0] = new PTid().X;
            o[1] = new HT<PTid>().V.X;
            PTid t = new();
            o[2] = t.X;
            o[3] = buf.Run();
            o[4] = Scaled;
            o[5] = buf[1];
            Buf negated = -buf;
            int converted = buf;
            o[6] = converted + negated.Data.Length;
        }
    }
}
