using System.Diagnostics;

namespace Kassyi.GSharp.Stdlib.Math.IO;

/// <summary>
/// Provides high-performance GPU-accelerated Divide and Conquer (D&amp;C) radix conversion
/// for streaming arbitrary-precision <see cref="GpuUIntBigInteger"/> to decimal text.
/// </summary>
public static class GpuRadixConverter
{
    private const int LeafThresholdDigits = 10_000;

    /// <summary>
    /// Converts a <see cref="GpuUIntBigInteger"/> to decimal digits using GPU-accelerated divide-and-conquer division,
    /// streaming the output directly to a <see cref="TextWriter"/>.
    /// </summary>
    /// <param name="context">The GPU computation context.</param>
    /// <param name="value">The integer to convert.</param>
    /// <param name="targetDigits">The number of digits to generate.</param>
    /// <param name="writer">The output stream writer.</param>
    /// <param name="isMostSignificant">True if formatting the integer part with a decimal point (e.g. "3.1415...").</param>
    public static void ConvertAndWrite(
        GpuContext context,
        GpuUIntBigInteger value,
        int targetDigits,
        TextWriter writer,
        bool isMostSignificant = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetDigits);

        var radixCache = new Dictionary<int, GpuUIntBigInteger>();
        var activePointers = new List<GpuUIntBigInteger>();

        try
        {
            ConvertAndWriteInternal(context, value, targetDigits, writer, radixCache, activePointers, isMostSignificant);
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
        GpuContext context,
        GpuUIntBigInteger value,
        int targetDigits,
        TextWriter writer,
        Dictionary<int, GpuUIntBigInteger> radixCache,
        List<GpuUIntBigInteger> activePointers,
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

            divisorGpu = GpuBigIntMath.Pow10(context, lowerDigits);
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
        var targetWords = System.Math.Max(actualWords, divisorGpu.NumWords);
        using var paddedValue = value.PadTo(targetWords);
        using var paddedDivisor = divisorGpu.PadTo(targetWords);

        using var quotient = new GpuUIntBigInteger(context, targetWords);
        using var remainder = new GpuUIntBigInteger(context, targetWords);

        GpuBigIntDivRem.DivRem(paddedValue, paddedDivisor, quotient, remainder);

        ConvertAndWriteInternal(context, quotient, upperDigits, writer, radixCache, activePointers, isMostSignificant);
        ConvertAndWriteInternal(context, remainder, lowerDigits, writer, radixCache, activePointers, false);
    }
}
