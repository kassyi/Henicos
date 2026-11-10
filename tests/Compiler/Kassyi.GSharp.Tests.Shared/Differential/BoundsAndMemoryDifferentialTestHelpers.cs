#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Shared test constants, helpers, and naming utilities for bounds and memory differential tests.
/// </summary>
internal static class BoundsAndMemoryDifferentialTestHelpers
{
    internal const int DefaultRows = 64;
    internal const int SimultaneousRows = 256;
    internal const int CanaryStart = BoundsAndMemoryDifferentialKernels.CanaryPad;
    internal const int CanaryLen = BoundsAndMemoryDifferentialKernels.CanarySliceLength;
    internal const int CanaryTotal = BoundsAndMemoryDifferentialKernels.CanaryBackingLength;
    internal const int SharedTileLen = BoundsAndMemoryDifferentialKernels.SharedTileLength;

    /// <summary>
    /// Builds the case name of a kernel of <see cref="BoundsAndMemoryDifferentialKernels"/>.
    /// </summary>
    internal static string KernelName(string kernelMemberName, string? suffix = null) =>
        DiffKernelNames.Of(typeof(BoundsAndMemoryDifferentialKernels), kernelMemberName, suffix);

    // Reading a null array throws NullReferenceException on every path (#1409). CUDA reports the place of the receiver
    // (line and column in BoundsAndMemoryDifferentialKernels.cu.cs) through the kernel's error site table; the managed
    // path throws .NET's own exception, which carries no place, so the place is checked where CUDA is the only path
    internal static ExceptionPolicy NullArrayRead(int line, int column) =>
#if GSHARP_CUDA
        new(typeof(NullReferenceException), DiffPaths.CheckedPaths())
        {
            SourceFile = nameof(BoundsAndMemoryDifferentialKernels),
            Line = line,
            Column = column,
        };
#else
        new(typeof(NullReferenceException), DiffPaths.CheckedPaths());
#endif

    // A null passed where a device function parameter is not nullable: NullReferenceException on every path; CUDA, the
    // only path of Tests.Cuda, reports the place of the argument
    internal static ExceptionPolicy NullArrayArgument(int line, int column) =>
#if GSHARP_CUDA
        new(typeof(NullReferenceException), DiffPaths.CheckedPaths())
        {
            SourceFile = nameof(BoundsAndMemoryDifferentialKernels),
            Line = line,
            Column = column,
        };
#else
        new(typeof(NullReferenceException), DiffPaths.CheckedPaths());
#endif

    internal static int[] CreateCanaryBacking()
    {
        var backing = new int[CanaryTotal];
        for (int i = 0; i < CanaryTotal; i++)
        {
            backing[i] = (i < CanaryStart || i >= CanaryStart + CanaryLen)
                ? -10000 - i
                : (i - CanaryStart + 1) * 100;
        }

        return backing;
    }
}
