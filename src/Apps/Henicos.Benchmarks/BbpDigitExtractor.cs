using System.Text;

namespace Henicos.Benchmarks;

/// <summary>
/// Implements the Bailey-Borwein-Plouffe (BBP) spigot algorithm for direct extraction of arbitrary hexadecimal Pi digits.
/// Reference: David H. Bailey's official piqpr8.c implementation.
/// </summary>
public static class BbpDigitExtractor
{
    private const double Epsilon = 1e-17;
    private const int SafeChunkSize = 8; // Max digits extracted in a single double precision pass

    /// <summary>
    /// Computes the sequence of hexadecimal digits of Pi immediately following the specified position (id).
    /// For example, id = 0 returns digits starting at the 1st hexadecimal fractional digit ("243F6A8885A308D3...").
    /// Automatically chunks requests exceeding 8 digits to maintain precision across arbitrary lengths.
    /// </summary>
    /// <param name="id">The 0-indexed fractional digit position offset.</param>
    /// <param name="digitCount">Total number of hexadecimal digits to extract.</param>
    /// <returns>A string containing the extracted hexadecimal digits in uppercase.</returns>
    public static string ExtractHexDigits(int id, int digitCount = 16)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Digit position must be non-negative.");
        }

        if (digitCount <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(digitCount);
        var currentOffset = id;
        var remaining = digitCount;

        while (remaining > 0)
        {
            var take = Math.Min(remaining, SafeChunkSize);
            var chunk = ExtractChunk(currentOffset, take);
            sb.Append(chunk);

            currentOffset += take;
            remaining -= take;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts up to 8 hexadecimal digits starting directly after the specified offset.
    /// Uses parallel reduction when the offset is sufficiently large.
    /// </summary>
    private static string ExtractChunk(int id, int count)
    {
        var s1 = Series(1, id);
        var s2 = Series(4, id);
        var s3 = Series(5, id);
        var s4 = Series(6, id);

        var pid = 4.0 * s1 - 2.0 * s2 - s3 - s4;
        pid -= Math.Floor(pid);
        if (pid < 0.0)
        {
            pid += 1.0;
        }

        var hexChars = new char[count];
        for (var i = 0; i < count; i++)
        {
            pid = 16.0 * (pid - Math.Floor(pid));
            var digit = (int)pid;
            hexChars[i] = digit < 10 ? (char)('0' + digit) : (char)('A' + digit - 10);
        }

        return new string(hexChars);
    }

    private static double Series(int m, int id)
    {
        var s = 0.0;

        // Sum the series up to id (Parallel execution for large id)
        if (id >= 10000)
        {
            var lockObj = new object();
            Parallel.For(0, id + 1, () => 0.0, (k, _, localSum) =>
                {
                    var ak = 8L * k + m;
                    long p = id - k;
                    var t = ModExp16(p, ak);
                    localSum += (double)t / ak;
                    return localSum - Math.Floor(localSum);
                },
                localSum =>
                {
                    lock (lockObj)
                    {
                        s += localSum;
                        s -= Math.Floor(s);
                    }
                });
        }
        else
        {
            for (var k = 0; k <= id; k++)
            {
                var ak = 8L * k + m;
                long p = id - k;
                var t = ModExp16(p, ak);
                s += (double)t / ak;
                s -= Math.Floor(s);
            }
        }

        // Compute terms where k > id (Converges extremely fast; typically <= 25 terms)
        for (var k = id + 1; ; k++)
        {
            var ak = 8L * k + m;
            var t = Math.Pow(16.0, id - k) / ak;
            if (t < Epsilon)
            {
                break;
            }

            s += t;
            s -= Math.Floor(s);
        }

        return s;
    }

    /// <summary>
    /// Computes (16^p) mod ak accurately using standard binary exponentiation.
    /// </summary>
    private static long ModExp16(long p, long ak)
    {
        if (ak == 1)
        {
            return 0;
        }

        var baseVal = 16UL % (ulong)ak;
        ulong result = 1;
        var exp = p;

        while (exp > 0)
        {
            if ((exp & 1) == 1)
            {
                result = (result * baseVal) % (ulong)ak;
            }

            baseVal = (baseVal * baseVal) % (ulong)ak;
            exp >>= 1;
        }

        return (long)result;
    }
}
