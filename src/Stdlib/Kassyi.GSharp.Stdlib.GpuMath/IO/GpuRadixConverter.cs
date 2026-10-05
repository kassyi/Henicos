using System.Diagnostics;

namespace Kassyi.GSharp.Stdlib.GpuMath.IO;

/// <summary>
/// Provides high-performance GPU-accelerated Divide-and-Conquer (D&amp;C) radix conversion
/// for streaming arbitrary-precision <see cref="BigNum.GpuBigUInt"/> to decimal text.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Serial base-10 radix conversion of multi-million-digit numbers is inherently $O(N^2)$ on CPU.
/// <see cref="GpuRadixConverter"/> splits the integer into sub-radix segments using GPU-accelerated $O(M(N)\log N)$ NTT division,
/// switching to fast CPU formatting at a configurable leaf threshold.
/// </para>
/// <para>
/// <b>Performance:</b> Achieves linearithmic radix conversion scaling up to 100M+ decimal digits.
/// </para>
/// </remarks>
public static class GpuRadixConverter
{
    private const int LeafThresholdDigits = 10_000;

    /// <summary>
    /// Converts an arbitrary-precision unsigned integer (<see cref="BigNum.GpuBigUInt"/>) to decimal digits using GPU-accelerated divide-and-conquer division,
    /// streaming the formatted characters directly to the specified <see cref="TextWriter"/>.
    /// </summary>
    /// <param name="workspace">The GPU computation workspace managing memory and kernel dispatches.</param>
    /// <param name="value">The unsigned large integer to convert.</param>
    /// <param name="targetDigits">The expected or target number of decimal digits to generate.</param>
    /// <param name="writer">The output stream writer receiving the formatted decimal text.</param>
    /// <param name="isMostSignificant"><see langword="true"/> if formatting the integer part with a decimal point (e.g. <c>"3.1415..."</c>); otherwise, <see langword="false"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="targetDigits"/> is less than or equal to zero.</exception>
    public static void ConvertAndWrite(
        GpuWorkspace workspace,
        GpuBigUInt value,
        int targetDigits,
        TextWriter writer,
        bool isMostSignificant = false)
    {

        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetDigits);

        var radixCache = new Dictionary<int, GpuBigUInt>();
        var activePointers = new List<GpuBigUInt>();

        try
        {
            ConvertAndWriteInternal(workspace, value, targetDigits, writer, radixCache, activePointers, isMostSignificant);
        }
        finally
        {
            foreach (var ptr in activePointers)
            {
                ptr.Dispose();
            }
        }
    }

    private static void ConvertAndWriteInternal(
        GpuWorkspace workspace,
        GpuBigUInt value,
        int targetDigits,
        TextWriter writer,
        Dictionary<int, GpuBigUInt> radixCache,
        List<GpuBigUInt> activePointers,
        bool isMostSignificant)
    {
        var actualWords = (GpuBigIntDivRem.GetActualBits(value) + 31) / 32;
        if (actualWords == 0)
        {
            actualWords = 1;
        }

        // Leaf threshold: CPU formatting is fast enough for <= 10,000 digits.
        if (targetDigits <= LeafThresholdDigits)
        {
            var cpuVal = value.ToBigInteger();
            var str = cpuVal.ToString();

            if (!isMostSignificant && str.Length < targetDigits)
            {
                str = new string('0', targetDigits - str.Length) + str;
            }

            if (isMostSignificant && str.Length > 0)
            {
                str = str[..1] + "." + str[1..];
            }

            writer.Write(str);
            return;
        }

        var lowerDigits = targetDigits / 2;
        var upperDigits = targetDigits - lowerDigits;

        if (!radixCache.TryGetValue(lowerDigits, out var divisorGpu))
        {
            var swCache = Stopwatch.StartNew();
            if (lowerDigits > 1_000_000)
            {
                Logger.Info("RadixConverter", $"Radix Cache: Computing 10^{lowerDigits} on GPU...");
            }

            divisorGpu = GpuBigIntMath.Pow10(workspace, lowerDigits);
            radixCache[lowerDigits] = divisorGpu;
            activePointers.Add(divisorGpu);
            swCache.Stop();

            if (lowerDigits > 1_000_000)
            {
                Logger.Info("RadixConverter",
                    $"Radix Cache: 10^{lowerDigits} computed in {swCache.ElapsedMilliseconds} ms");
            }
        }

        // Align dimensions for GPU division
        var targetWords = Math.Max(actualWords, divisorGpu.NumWords);
        using var paddedValue = value.PadTo(targetWords);
        using var paddedDivisor = divisorGpu.PadTo(targetWords);

        using var quotient = new GpuBigUInt(workspace, targetWords);
        using var remainder = new GpuBigUInt(workspace, targetWords);

        GpuBigIntDivRem.DivRem(paddedValue, paddedDivisor, quotient, remainder);

        ConvertAndWriteInternal(workspace, quotient, upperDigits, writer, radixCache, activePointers, isMostSignificant);
        ConvertAndWriteInternal(workspace, remainder, lowerDigits, writer, radixCache, activePointers, false);
    }
}
