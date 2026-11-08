// ReSharper disable All
#pragma warning disable IDE0051, IDE0022

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

/// <summary>
/// The domain errors that kernels report through the device status words of a workspace: a result that does not fit in
/// the buffer the caller chose, or an unsigned result below zero. The host reports them as
/// <see cref="OverflowException"/> at the next read of the workspace stream.
/// </summary>
[SkipSnapshotConsistency("Tested in separate suites")]
[ExcludeFromCodeCoverage]
public static partial class GpuDomainErrors
{
    /// <summary>The status word that counts the domain errors; it only grows.</summary>
    public const int CountWord = 2;

    /// <summary>The status word that keeps the code of the last domain error.</summary>
    public const int CodeWord = 3;

    /// <summary>The sum of <c>GpuBigIntMath.Add(a, uint, r)</c> does not fit in <c>r</c>.</summary>
    public const uint ScalarAddOverflow = 1u;

    /// <summary>The difference of <c>GpuBigIntMath.Sub(a, uint, r)</c> is below zero.</summary>
    public const uint ScalarSubtractBelowZero = 2u;

    /// <summary>The product of <c>GpuBigIntMath.Mul(a, uint, r)</c> does not fit in <c>r</c>.</summary>
    public const uint ScalarMultiplyOverflow = 3u;

    /// <summary>A sum does not fit in the result buffer.</summary>
    public const uint AddOverflow = 4u;

    /// <summary>An unsigned difference is below zero.</summary>
    public const uint SubtractBelowZero = 5u;

    /// <summary>A difference does not fit in the result buffer.</summary>
    public const uint SubtractOverflow = 6u;

    /// <summary>A value does not fit in the word count it is fitted to.</summary>
    public const uint FitOverflow = 7u;

    /// <summary>
    /// Counts one domain error and records its code. Called by one thread per failed operation.
    /// </summary>
    /// <param name="status">The status words of the workspace.</param>
    /// <param name="code">The code of the operation that failed.</param>
    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Report(uint[] status, uint code)
    {
        Gpu.AtomicAdd(ref status[CountWord], 1u);
        status[CodeWord] = code;
    }
}
