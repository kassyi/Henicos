using System.Buffers;

namespace Henicos.Cli;

/// <summary>
/// CPU/GPU Hybrid Parallel Reduction Engine for multi-threaded modular reduction of NTT squaring output arrays.
/// </summary>
public static class HybridReduction
{
    /// <summary>
    /// Reduces a double-width input array (representing T = X^2) modulo N.
    /// Operates in-place on the input array to prevent massive allocations.
    /// </summary>
    /// <param name="data">The double-width input array from GPU (length >= 2 * numWords). Lower half will contain the result.</param>
    /// <param name="e">The Fermat parameter E</param>
    /// <param name="numWords">The number of uint words required to store N</param>
    public static void ReduceInPlace(uint[] data, int e, int numWords)
    {
        var bit2E1 = 2 * e + 1;
        var word2E1 = bit2E1 / 32;
        var rem2E1 = bit2E1 % 32;

        var wordE = e / 32;
        var remE = e % 32;

        var rMask = (rem2E1 == 0) ? 0xFFFFFFFF : (1U << rem2E1) - 1;

        for (var pass = 0; pass < 2; pass++)
        {
            // [WHY] Direct in-place mutation causes race conditions and destructive overwrites when word2E1 < numWords,
            // because reading the upper quotient Q overlaps with writing the remainder R. Renting from ArrayPool ensures thread safety without heap allocations.
            var nextData = ArrayPool<uint>.Shared.Rent(data.Length);
            try
            {
                // R starts as the lower 2E+1 bits of data.
                var rCopyWords = Math.Min(data.Length, word2E1 + 1);
                Array.Copy(data, nextData, rCopyWords);
                if (rem2E1 != 0 && rCopyWords > 0)
                {
                    nextData[word2E1] &= rMask;
                }

                // We must iterate over all words where Q might contribute.
                // (Q << E) shifts bits up. If Q is max 2E+1 bits, Q<<E is 3E+1 bits.
                // To be safe, we just process all available words up to data.Length.
                var targetWords = data.Length;
                var chunkSize = Math.Max(1024, targetWords / Math.Max(1, Environment.ProcessorCount));
                var numChunks = (targetWords + chunkSize - 1) / chunkSize;

                var carryBuffer = new long[numChunks + 1];

                Parallel.For(0, numChunks, chunkIdx =>
                {
                    var start = chunkIdx * chunkSize;
                    var end = Math.Min(start + chunkSize, targetWords);

                    long localCarry = 0;

                    for (var i = start; i < end; i++)
                    {
                        var current = nextData[i] + localCarry;

                        // Add (Q << E)
                        // (Q << E) has its i-th word coming from Q's (i - wordE) shifted.
                        var qShiftE = GetShiftedQWord(data, word2E1, rem2E1, i, wordE, remE, data.Length);
                        current += qShiftE;

                        // Subtract (Q * 11)
                        // Q * 11 = (Q << 3) + (Q << 1) + Q
                        // We need the i-th word of (Q * 11).
                        // This is simply: (i-th word of Q<<3) + (i-th word of Q<<1) + (i-th word of Q)
                        var qWord = GetShiftedQWord(data, word2E1, rem2E1, i, 0, 0, data.Length);
                        var qShift1 = GetShiftedQWord(data, word2E1, rem2E1, i, 0, 1, data.Length);
                        var qShift3 = GetShiftedQWord(data, word2E1, rem2E1, i, 0, 3, data.Length);

                        current -= qWord;
                        current -= qShift1;
                        current -= qShift3;

                        nextData[i] = (uint)(current & 0xFFFFFFFF);
                        localCarry = current >> 32;
                    }
                    carryBuffer[chunkIdx + 1] = localCarry;
                });

                long rippleCarry = 0;
                for (var chunkIdx = 0; chunkIdx < numChunks; chunkIdx++)
                {
                    var start = chunkIdx * chunkSize;
                    var end = Math.Min(start + chunkSize, targetWords);

                    rippleCarry += carryBuffer[chunkIdx];
                    if (rippleCarry == 0)
                    {
                        continue;
                    }

                    for (var i = start; i < end; i++)
                    {
                        var current = nextData[i] + rippleCarry;
                        nextData[i] = (uint)(current & 0xFFFFFFFF);
                        rippleCarry = current >> 32;
                        if (rippleCarry == 0)
                        {
                            break;
                        }
                    }
                }
                // Apply nextData back to data
                Array.Copy(nextData, data, data.Length);

                // The entire array `data` now holds the correctly reduced value T'' in 2's complement.
                // No further manual overwrites are needed.
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(nextData);
            }
        }
    }

    /// <summary>
    /// Gets the targetIndex-th word of (Q &lt;&lt; (wordShift*32 + bitShift))
    /// </summary>
    private static uint GetShiftedQWord(uint[] data, int qWordStart, int qBitStart, int targetIndex, int wordShift, int bitShift, int dataLen)
    {
        // We want the targetIndex-th word of (Q << (wordShift*32 + bitShift)).
        // This corresponds to Q's (targetIndex - wordShift) word, but shifted left by bitShift.
        var qTargetWord = targetIndex - wordShift;
        if (qTargetWord < 0)
        {
            return 0;
        }

        // Q itself is represented as bits starting from data[qWordStart] at bit offset qBitStart.
        // We need to read 32 bits from (Q << bitShift) for the word qTargetWord.
        // Effectively, we want 32 bits from Q starting at bit index: qTargetWord * 32 - bitShift.
        // Total bit index in Q = qTargetWord * 32 - bitShift.
        var totalBitIndexInQ = (long)qTargetWord * 32 - bitShift;

        if (totalBitIndexInQ < 0)
        {
            // We are asking for bits that are shifted in as zeros.
            // However, we might still intersect the lowest bits of Q.
            // E.g. bitShift = 3, targetWord = 0 -> totalBitIndex = -3.
            // The result is the lowest 29 bits of Q, shifted left by 3.
            var q0 = GetRawQWord(data, qWordStart, qBitStart, 0, dataLen);
            return q0 << bitShift;
        }

        var qWordIdx = (int)(totalBitIndexInQ / 32);
        var qBitRem = (int)(totalBitIndexInQ % 32);

        var qw0 = GetRawQWord(data, qWordStart, qBitStart, qWordIdx, dataLen);
        var qw1 = GetRawQWord(data, qWordStart, qBitStart, qWordIdx + 1, dataLen);

        if (qBitRem == 0)
        {
            return qw0;
        }

        return (qw0 >> qBitRem) | (qw1 << (32 - qBitRem));
    }

    private static uint GetRawQWord(uint[] data, int qWordStart, int qBitStart, int qIndex, int dataLen)
    {
        var w0Idx = qWordStart + qIndex;
        var w1Idx = w0Idx + 1;

        var w0 = (w0Idx < dataLen && w0Idx >= 0) ? data[w0Idx] : 0;
        var w1 = (w1Idx < dataLen && w1Idx >= 0) ? data[w1Idx] : 0;

        if (qBitStart == 0)
        {
            return w0;
        }

        return (w0 >> qBitStart) | (w1 << (32 - qBitStart));
    }
}
