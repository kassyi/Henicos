using Kassyi.GSharp.Core;
using Kassyi.ZeroAlloc.Linq;

namespace Kassyi.GSharp.Tests.Shared.Runtime;

/// <summary>
/// The position stages of a fused StackLinq chain (Skip, Take, SkipLast, TakeLast) give the range of C# (#624 R1, #677),
/// and each argument is evaluated once.
/// </summary>
public static partial class StackLinqRangeKernels
{
    // Every thread evaluates the chain sequentially and with AsParallel()
    [GpuKernel]
    public static void TwoStageRangeKernel(int[] data, int combo, int a, int b, int[] result)
    {
        int gtid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int sequential = -1;
        int parallel = -1;
        if (combo == 0)
        {
            sequential = data.AsStackEnumerable().Skip(a).Skip(b).Sum();
            parallel = data.AsStackEnumerable().Skip(a).Skip(b).AsParallel().Sum();
        }
        else if (combo == 1)
        {
            sequential = data.AsStackEnumerable().Skip(a).Take(b).Sum();
            parallel = data.AsStackEnumerable().Skip(a).Take(b).AsParallel().Sum();
        }
        else if (combo == 2)
        {
            sequential = data.AsStackEnumerable().Take(a).Skip(b).Sum();
            parallel = data.AsStackEnumerable().Take(a).Skip(b).AsParallel().Sum();
        }
        else if (combo == 3)
        {
            sequential = data.AsStackEnumerable().Take(a).Take(b).Sum();
            parallel = data.AsStackEnumerable().Take(a).Take(b).AsParallel().Sum();
        }
        else if (combo == 4)
        {
            sequential = data.AsStackEnumerable().SkipLast(a).Skip(b).Sum();
            parallel = data.AsStackEnumerable().SkipLast(a).Skip(b).AsParallel().Sum();
        }
        else if (combo == 5)
        {
            sequential = data.AsStackEnumerable().SkipLast(a).Take(b).Sum();
            parallel = data.AsStackEnumerable().SkipLast(a).Take(b).AsParallel().Sum();
        }
        else if (combo == 6)
        {
            sequential = data.AsStackEnumerable().TakeLast(a).Skip(b).Sum();
            parallel = data.AsStackEnumerable().TakeLast(a).Skip(b).AsParallel().Sum();
        }
        else if (combo == 7)
        {
            sequential = data.AsStackEnumerable().TakeLast(a).Take(b).Sum();
            parallel = data.AsStackEnumerable().TakeLast(a).Take(b).AsParallel().Sum();
        }
        else if (combo == 8)
        {
            sequential = data.AsStackEnumerable().SkipLast(a).SkipLast(b).Sum();
            parallel = data.AsStackEnumerable().SkipLast(a).SkipLast(b).AsParallel().Sum();
        }
        else if (combo == 9)
        {
            sequential = data.AsStackEnumerable().SkipLast(a).TakeLast(b).Sum();
            parallel = data.AsStackEnumerable().SkipLast(a).TakeLast(b).AsParallel().Sum();
        }
        else if (combo == 10)
        {
            sequential = data.AsStackEnumerable().TakeLast(a).SkipLast(b).Sum();
            parallel = data.AsStackEnumerable().TakeLast(a).SkipLast(b).AsParallel().Sum();
        }
        else if (combo == 11)
        {
            sequential = data.AsStackEnumerable().TakeLast(a).TakeLast(b).Sum();
            parallel = data.AsStackEnumerable().TakeLast(a).TakeLast(b).AsParallel().Sum();
        }
        result[gtid * 2] = sequential;
        result[gtid * 2 + 1] = parallel;
    }

    // A position stage after Where counts the elements that pass, with its argument evaluated once
    [GpuKernel]
    public static void FilteredTakeKernel(int[] data, int[] counter, int[] result)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            result[0] = data.AsStackEnumerable().Where(x => x % 2 == 0).Take(CountedThree(counter)).Sum();
            result[1] = data.AsStackEnumerable().Select(x => x * 10).Skip(CountedThree(counter)).Sum();
        }
    }

    // Returns 3 and counts its calls
    [GpuDeviceFunction]
    public static int CountedThree(int[] counter)
    {
        counter[0] = counter[0] + 1;
        return 3;
    }

    // Second review (M1): C# calls the selector for the elements a following Skip drops, so the Skip is not folded
    [GpuKernel]
    public static void SelectSkipCountKernel(int[] data, int[] counter, int[] result)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            result[0] = data.AsStackEnumerable().Select(x => System.Threading.Interlocked.Add(ref counter[0], 1) * 0 + x).Skip(3).Sum();
        }
    }

    // Take(n) after a stage that runs in the loop stops once it has given n elements: C# calls the selector or the
    // predicate before it n times (or up to the n-th passing element), and not at all for Take(0)
    [GpuKernel]
    public static void TakeCountKernel(int[] data, int[] counter, int take, int[] result)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            result[0] = data.AsStackEnumerable().Select(x => System.Threading.Interlocked.Add(ref counter[0], 1) * 0 + x).Take(take).Sum();
            result[1] = data.AsStackEnumerable().Where(x => System.Threading.Interlocked.Add(ref counter[1], 1) > 0 && x % 2 == 0).Take(take).Sum();
        }
    }

    // Take(a) followed by a stage that passes over elements (Skip, SkipWhile): once the Take has given a elements, C# does
    // not call the selector or predicate before it again, also when the Skip passed over the last one
    [GpuKernel]
    public static void TakeSkipCountKernel(int[] data, int[] counter, int take, int skip, int[] result)
    {
        if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)
        {
            result[0] = data.AsStackEnumerable().Select(x => System.Threading.Interlocked.Add(ref counter[0], 1) * 0 + x).Take(take).Skip(skip).Sum();
            result[1] = data.AsStackEnumerable().Where(x => System.Threading.Interlocked.Add(ref counter[1], 1) > 0 && x % 2 == 0).Take(take).Skip(skip).Sum();
            result[2] = data.AsStackEnumerable().Select(x => System.Threading.Interlocked.Add(ref counter[2], 1) * 0 + x).Take(take).SkipWhile(x => x <= skip).Sum();
        }
    }

    /// <summary>The number of three-stage chains of <see cref="ThreeStageSweepKernel"/>.</summary>
    public const int ThreeStageChains = 32;

    // Second review (M2): every three-stage chain, sequential (even slot) and AsParallel() (odd slot)
    [GpuKernel]
    public static void ThreeStageSweepKernel(int[] data, int a, int b, int c, int[] result)
    {
        int g = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        result[g * 64 + 0] = data.AsStackEnumerable().Skip(a).Skip(b).Skip(c).Sum();
        result[g * 64 + 1] = data.AsStackEnumerable().Skip(a).Skip(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 2] = data.AsStackEnumerable().Skip(a).Skip(b).Take(c).Sum();
        result[g * 64 + 3] = data.AsStackEnumerable().Skip(a).Skip(b).Take(c).AsParallel().Sum();
        result[g * 64 + 4] = data.AsStackEnumerable().Skip(a).Take(b).Skip(c).Sum();
        result[g * 64 + 5] = data.AsStackEnumerable().Skip(a).Take(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 6] = data.AsStackEnumerable().Skip(a).Take(b).Take(c).Sum();
        result[g * 64 + 7] = data.AsStackEnumerable().Skip(a).Take(b).Take(c).AsParallel().Sum();
        result[g * 64 + 8] = data.AsStackEnumerable().Take(a).Skip(b).Skip(c).Sum();
        result[g * 64 + 9] = data.AsStackEnumerable().Take(a).Skip(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 10] = data.AsStackEnumerable().Take(a).Skip(b).Take(c).Sum();
        result[g * 64 + 11] = data.AsStackEnumerable().Take(a).Skip(b).Take(c).AsParallel().Sum();
        result[g * 64 + 12] = data.AsStackEnumerable().Take(a).Take(b).Skip(c).Sum();
        result[g * 64 + 13] = data.AsStackEnumerable().Take(a).Take(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 14] = data.AsStackEnumerable().Take(a).Take(b).Take(c).Sum();
        result[g * 64 + 15] = data.AsStackEnumerable().Take(a).Take(b).Take(c).AsParallel().Sum();
        result[g * 64 + 16] = data.AsStackEnumerable().SkipLast(a).Skip(b).Skip(c).Sum();
        result[g * 64 + 17] = data.AsStackEnumerable().SkipLast(a).Skip(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 18] = data.AsStackEnumerable().SkipLast(a).Skip(b).Take(c).Sum();
        result[g * 64 + 19] = data.AsStackEnumerable().SkipLast(a).Skip(b).Take(c).AsParallel().Sum();
        result[g * 64 + 20] = data.AsStackEnumerable().SkipLast(a).Take(b).Skip(c).Sum();
        result[g * 64 + 21] = data.AsStackEnumerable().SkipLast(a).Take(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 22] = data.AsStackEnumerable().SkipLast(a).Take(b).Take(c).Sum();
        result[g * 64 + 23] = data.AsStackEnumerable().SkipLast(a).Take(b).Take(c).AsParallel().Sum();
        result[g * 64 + 24] = data.AsStackEnumerable().SkipLast(a).SkipLast(b).Skip(c).Sum();
        result[g * 64 + 25] = data.AsStackEnumerable().SkipLast(a).SkipLast(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 26] = data.AsStackEnumerable().SkipLast(a).SkipLast(b).Take(c).Sum();
        result[g * 64 + 27] = data.AsStackEnumerable().SkipLast(a).SkipLast(b).Take(c).AsParallel().Sum();
        result[g * 64 + 28] = data.AsStackEnumerable().SkipLast(a).SkipLast(b).SkipLast(c).Sum();
        result[g * 64 + 29] = data.AsStackEnumerable().SkipLast(a).SkipLast(b).SkipLast(c).AsParallel().Sum();
        result[g * 64 + 30] = data.AsStackEnumerable().SkipLast(a).SkipLast(b).TakeLast(c).Sum();
        result[g * 64 + 31] = data.AsStackEnumerable().SkipLast(a).SkipLast(b).TakeLast(c).AsParallel().Sum();
        result[g * 64 + 32] = data.AsStackEnumerable().SkipLast(a).TakeLast(b).Skip(c).Sum();
        result[g * 64 + 33] = data.AsStackEnumerable().SkipLast(a).TakeLast(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 34] = data.AsStackEnumerable().SkipLast(a).TakeLast(b).Take(c).Sum();
        result[g * 64 + 35] = data.AsStackEnumerable().SkipLast(a).TakeLast(b).Take(c).AsParallel().Sum();
        result[g * 64 + 36] = data.AsStackEnumerable().SkipLast(a).TakeLast(b).SkipLast(c).Sum();
        result[g * 64 + 37] = data.AsStackEnumerable().SkipLast(a).TakeLast(b).SkipLast(c).AsParallel().Sum();
        result[g * 64 + 38] = data.AsStackEnumerable().SkipLast(a).TakeLast(b).TakeLast(c).Sum();
        result[g * 64 + 39] = data.AsStackEnumerable().SkipLast(a).TakeLast(b).TakeLast(c).AsParallel().Sum();
        result[g * 64 + 40] = data.AsStackEnumerable().TakeLast(a).Skip(b).Skip(c).Sum();
        result[g * 64 + 41] = data.AsStackEnumerable().TakeLast(a).Skip(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 42] = data.AsStackEnumerable().TakeLast(a).Skip(b).Take(c).Sum();
        result[g * 64 + 43] = data.AsStackEnumerable().TakeLast(a).Skip(b).Take(c).AsParallel().Sum();
        result[g * 64 + 44] = data.AsStackEnumerable().TakeLast(a).Take(b).Skip(c).Sum();
        result[g * 64 + 45] = data.AsStackEnumerable().TakeLast(a).Take(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 46] = data.AsStackEnumerable().TakeLast(a).Take(b).Take(c).Sum();
        result[g * 64 + 47] = data.AsStackEnumerable().TakeLast(a).Take(b).Take(c).AsParallel().Sum();
        result[g * 64 + 48] = data.AsStackEnumerable().TakeLast(a).SkipLast(b).Skip(c).Sum();
        result[g * 64 + 49] = data.AsStackEnumerable().TakeLast(a).SkipLast(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 50] = data.AsStackEnumerable().TakeLast(a).SkipLast(b).Take(c).Sum();
        result[g * 64 + 51] = data.AsStackEnumerable().TakeLast(a).SkipLast(b).Take(c).AsParallel().Sum();
        result[g * 64 + 52] = data.AsStackEnumerable().TakeLast(a).SkipLast(b).SkipLast(c).Sum();
        result[g * 64 + 53] = data.AsStackEnumerable().TakeLast(a).SkipLast(b).SkipLast(c).AsParallel().Sum();
        result[g * 64 + 54] = data.AsStackEnumerable().TakeLast(a).SkipLast(b).TakeLast(c).Sum();
        result[g * 64 + 55] = data.AsStackEnumerable().TakeLast(a).SkipLast(b).TakeLast(c).AsParallel().Sum();
        result[g * 64 + 56] = data.AsStackEnumerable().TakeLast(a).TakeLast(b).Skip(c).Sum();
        result[g * 64 + 57] = data.AsStackEnumerable().TakeLast(a).TakeLast(b).Skip(c).AsParallel().Sum();
        result[g * 64 + 58] = data.AsStackEnumerable().TakeLast(a).TakeLast(b).Take(c).Sum();
        result[g * 64 + 59] = data.AsStackEnumerable().TakeLast(a).TakeLast(b).Take(c).AsParallel().Sum();
        result[g * 64 + 60] = data.AsStackEnumerable().TakeLast(a).TakeLast(b).SkipLast(c).Sum();
        result[g * 64 + 61] = data.AsStackEnumerable().TakeLast(a).TakeLast(b).SkipLast(c).AsParallel().Sum();
        result[g * 64 + 62] = data.AsStackEnumerable().TakeLast(a).TakeLast(b).TakeLast(c).Sum();
        result[g * 64 + 63] = data.AsStackEnumerable().TakeLast(a).TakeLast(b).TakeLast(c).AsParallel().Sum();
    }
}
