using System;
using Xunit;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

internal static class SpanWriteTestHelpers
{
    public const uint GridDimX = 1;
    public const uint BlockDimX = 1;

    public static TheoryData<bool> Paths => new() { false, true };

    public static TheoryData<int, int, bool> SliceWindowCases()
    {
        var data = new TheoryData<int, int, bool>();
        (int Offset, int Count)[] windows =
            [(0, 0), (5, 0), (20, 0), (0, 1), (7, 1), (19, 1), (0, 2), (3, 5), (2, 16), (1, 17), (0, 20)];
        foreach (var managed in new[] { false, true })
        {
            foreach (var (offset, count) in windows)
            {
                data.Add(offset, count, managed);
            }
        }

        return data;
    }

    public static TheoryData<int, int, int, bool> OverlapCopyCases()
    {
        var data = new TheoryData<int, int, int, bool>();
        (int SrcOffset, int DstOffset, int Count)[] cases =
        [
            (0, 0, 0),
            (4, 4, 0),
            (2, 2, 1),
            (2, 2, 7),
            // Forward overlap: srcOffset < dstOffset < srcOffset + count (requires backward copy)
            (1, 2, 6),
            (1, 3, 6),
            (0, 4, 8),
            // Backward overlap: dstOffset < srcOffset < dstOffset + count (requires forward copy)
            (2, 1, 6),
            (3, 1, 6),
            (4, 0, 8),
            // Disjoint in same buffer
            (0, 10, 6),
            (10, 0, 6),
        ];
        foreach (var managed in new[] { false, true })
        {
            foreach (var (srcOffset, dstOffset, count) in cases)
            {
                data.Add(srcOffset, dstOffset, count, managed);
            }
        }

        return data;
    }

    public static void Run(bool isCudaDialect, bool managed, Action launch)
    {
        Skip.If(managed && isCudaDialect, "The managed path runs on the CPU test assembly.");
#if GSHARP_CPU
        if (managed)
        {
            ManagedKernelRun.Run(launch);
            return;
        }
#endif
        launch();
    }

    public static int[] CreateSequential(int length, int start)
    {
        var array = new int[length];
        for (var i = 0; i < length; i++)
        {
            array[i] = start + i;
        }

        return array;
    }
}
