using Kassyi.ZeroAlloc.Linq;

namespace Henicos.Benchmarks;

[SkipSnapshotConsistency]
public partial class StackLinqGpuKernels
{
    // ==========================================
    // GPU カーネル: Heavy Pipeline
    // ==========================================

    [GpuKernel]
    public static void RawFor_HeavyPipeline(int[] data, int[] results)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= results.Length)
        {
            return;
        }

        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            int x = data[i];
            if (x % 2 == 0)
            {
                sum += x * 3;
            }
        }

        results[tid] = sum;
    }

    [GpuKernel]
    public static void StackLinq_HeavyPipeline(int[] data, int[] results)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= results.Length)
        {
            return;
        }

        int sum = data.AsStackEnumerable()
            .Where(x => x % 2 == 0)
            .Select(x => x * 3)
            .Sum();

        results[tid] = sum;
    }

    // ==========================================
    // GPU カーネル: Data Windowing
    // ==========================================

    [GpuKernel]
    public static void RawFor_DataWindowing(int[] data, int endMarker, int[] results)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= results.Length)
        {
            return;
        }

        int sum = 0;
        bool paddingSkipped = false;

        for (int i = 0; i < data.Length; i++)
        {
            int x = data[i];

            // SkipWhile(x == 0)
            if (!paddingSkipped)
            {
                if (x == 0)
                {
                    continue;
                }

                paddingSkipped = true;
            }

            // TakeWhile(x != endMarker)
            if (x == endMarker)
            {
                break;
            }

            sum += x;
        }

        results[tid] = sum;
    }

    [GpuKernel]
    public static void StackLinq_DataWindowing(int[] data, int endMarker, int[] results)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= results.Length)
        {
            return;
        }

        int sum = data.AsStackEnumerable()
            .SkipWhile(x => x == 0)
            .TakeWhile(x => x != endMarker)
            .Sum();

        results[tid] = sum;
    }
}
