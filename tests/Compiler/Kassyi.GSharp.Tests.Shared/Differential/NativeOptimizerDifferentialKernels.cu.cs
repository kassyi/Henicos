#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for integer shapes that the MSVC global optimizer (<c>/Og</c>, part of <c>/O2</c>) miscompiles
/// on the native path (#1078): a wrapping negation used as a dividend, a <c>short</c> widened both through
/// <c>uint</c> and directly to <c>ulong</c>, and a wrapping <c>short</c> increment whose value then picks a
/// <c>ref</c> target.
/// </summary>
public static partial class NativeOptimizerDifferentialKernels
{
    /// <summary>The columns of <see cref="NegateThenDivideKernel"/>.</summary>
    public static readonly string[] NegateThenDivideColumns =
    [
        "(ulong)(long)(-v/2)",
        "-v/2",
        "(0-v)/2",
        "-v/3",
        "-v%3",
    ];

    /// <summary>The columns of <see cref="ShortWidenedTwiceKernel"/>.</summary>
    public static readonly string[] ShortWidenedTwiceColumns =
    [
        "fnv((ulong)(uint)s,(ulong)s)",
    ];

    /// <summary>The columns of <see cref="ShortIncrementPicksRefTargetKernel"/>.</summary>
    public static readonly string[] ShortIncrementPicksRefTargetColumns =
    [
        "(short)(s+1)",
        "x",
        "y",
        "hash(src)",
    ];

    /// <summary>
    /// Negates <c>src[t]</c> (wrapping at <see cref="int.MinValue"/>) and divides it by a constant. MSVC <c>/Og</c>
    /// rewrites <c>-v / 2</c> to <c>-(v / 2)</c>, which is wrong for <see cref="int.MinValue"/>.
    /// </summary>
    [GpuKernel]
    public static void NegateThenDivideKernel(int[] src, ulong[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        int v = src[t];
        int baseIdx = t * 5;
        dst[baseIdx + 0] = (ulong)(long)(-v / 2);
        dst[baseIdx + 1] = (ulong)(-v / 2);
        dst[baseIdx + 2] = (ulong)((0 - v) / 2);
        dst[baseIdx + 3] = (ulong)(-v / 3);
        dst[baseIdx + 4] = (ulong)(-v % 3);
    }

    /// <summary>
    /// Hashes one <c>short</c> widened through <c>uint</c> and widened directly to <c>ulong</c> (both sign-extend).
    /// MSVC <c>/Og</c> reuses the wrong sign extension for one of them. Only the hash is stored: storing the two widened
    /// values as well makes MSVC keep both extensions, and the miscompile no longer shows.
    /// </summary>
    [GpuKernel]
    public static void ShortWidenedTwiceKernel(int[] src, ulong[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        short s = (short)src[t];
        ulong h = 14695981039346656037UL;
        h = (h ^ ((ulong)(uint)s + 1UL)) * 1099511628211UL;
        h = (h ^ ((ulong)s + 4UL)) * 1099511628211UL;

        dst[t] = h;
    }

    /// <summary>
    /// Increments a <c>short</c> (wrapping at <see cref="short.MaxValue"/>) and uses the result to pick which local a
    /// <c>ref</c> conditional writes. MSVC 19.51 and older drop the conversion back to <c>short</c> under <c>/Og</c>.
    /// The addition is made in <c>int</c> so that it goes through the wrapping add of the emitted C++; the
    /// <c>short + 1</c> form cannot overflow <c>int</c>, is emitted as a plain addition, and is compiled correctly.
    /// The kernel also hashes its input (fourth column) only to be large enough that MSVC keeps it out of line from its
    /// launcher, as it does with larger kernels: inlined into the launcher loop, the same form is compiled correctly.
    /// </summary>
    [GpuKernel]
    public static void ShortIncrementPicksRefTargetKernel(int[] src, int[] dst, int rows)
    {
        int t = (int)(BlockIndex.X * BlockDimension.X + ThreadIndex.X);
        if (t >= rows)
        {
            return;
        }

        short s = (short)src[t];
        int wide = s;
        s = (short)(wide + 1);
        int x = 10;
        int y = 20;
        (s <= 0 ? ref x : ref y) = 0;

        uint h = (uint)src[t];
        for (int round = 0; round < 4; round++)
        {
            h = (h ^ (h >> 16)) * 0x7FEB352Du;
            h = (h ^ (h >> 15)) * 0x846CA68Bu;
            h = (h ^ (h >> 13)) * 0x9E3779B1u;
            h = (h ^ (h >> 11)) * 0x85EBCA6Bu;
            h = (h ^ (h >> 7)) * 0xC2B2AE35u;
            h = (h ^ (h >> 5)) * 0x27D4EB2Fu;
            h = (h ^ (h >> 9)) * 0x165667B1u;
            h = (h ^ (h >> 3)) * 0xD3A2646Du;
            h = (h ^ (h >> 12)) * 0xFD7046C5u;
            h = (h ^ (h >> 14)) * 0xB55A4F09u;
            h = (h ^ (h >> 6)) * 0x2545F491u;
            h = (h ^ (h >> 10)) * 0x9E3779B9u;
        }

        int baseIdx = t * 4;
        dst[baseIdx + 0] = s;
        dst[baseIdx + 1] = x;
        dst[baseIdx + 2] = y;
        dst[baseIdx + 3] = (int)h;
    }
}
