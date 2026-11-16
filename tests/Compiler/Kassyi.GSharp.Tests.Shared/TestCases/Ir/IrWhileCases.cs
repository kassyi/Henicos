using System;
using Kassyi.GSharp.Core;

namespace IrWhileCases;

public static class Loops
{
    public static void WhileWithBarrier(int[] data, int n)
    {
        int i = 0;
        while (i < n)
        {
            data[i] = i;
            Gpu.SyncThreads();
            i++;
        }
    }

    public static void ShuffleInCondition(int[] data, int x)
    {
        while (Warp.ShuffleSync(0xffffffffu, x, 0) > data[0])
        {
            data[0]++;
        }
    }

    public static void BarrierThenShuffleLoop(int[] data, int x)
    {
        Gpu.SyncThreads();
        while (Warp.ShuffleSync(0xffffffffu, x, 0) > data[0])
        {
            data[0]++;
        }
    }

    public static void OutVarInCondition(int[] data)
    {
        while (TryNext(data, out var next))
        {
            data[0] = next;
        }
    }

    public static void DoWhile(int[] data)
    {
        do
        {
            data[0]--;
        } while (data[0] > 0);
    }

    public static void CheckedBlock(int[] data)
    {
        checked
        {
            data[0] = data[1] + data[2];
        }
    }

    public static void UncheckedBlock(int[] data)
    {
        unchecked
        {
            data[0] = data[1] * data[2];
        }
    }

    public static void Spelling(int[] data, int a, int b)
    {
        // first
        /* second */
        var sum = ((a + (b)));
        int named = 0x10;
        data[(0)] = sum * named + 1_000;
    }

    public static void ImplicitConversion(long[] data, int a)
    {
        data[0] = (a);
    }

    public static void Declarations(int[] data)
    {
        var deduced = data[0];
        int named = data[1];
        int first = 1, second = 2;
        TryNext(data, out var outDeduced);
        TryNext(data, out int outNamed);
        data[0] = deduced + named + first + second + outDeduced + outNamed;
    }

    public static void CommentedBarrier(int[] data)
    {
        // wait for the block
        Gpu.SyncThreads();
        data[0] = (Warp.ShuffleSync(0xffffffffu, data[0], 0));
    }

    public static void ConditionalShuffle(int[] data)
    {
        data[0] = data[1] > 0 ? Warp.ShuffleSync(0xffffffffu, data[1], 0) : 0;
    }

    public static void IfElse(int[] data)
    {
        if (data[0] > 0)
        {
            data[1] = 1;
        }
        else
        {
            data[1] = 2;
        }
    }

    public static void LogicalAnd(int[] data, bool a)
    {
        data[0] = a && data[1] > 0 ? 1 : 0;
    }

    public static void Coalesce(int?[] data)
    {
        data[0] = data[1] ?? 3;
    }

    public static void RefLocals(int[] data)
    {
        ref var deducedRef = ref data[0];
        ref int namedRef = ref data[1];
        deducedRef = namedRef;
    }

    public static void DeclaredWithoutInitializer(int[] data)
    {
        int later;
        later = data[0];
        data[1] = later;
    }

    private static bool TryNext(int[] data, out int next)
    {
        next = data[0] - 1;
        return next > 0;
    }
}
