#nullable enable
using System.Numerics;

namespace Kassyi.GSharp.Tests.Shared.Stdlib;

/// <summary>Converts between 32-bit words, least significant first, and <see cref="BigInteger"/>.</summary>
internal static class BigIntWords
{
    public static BigInteger ToBigInteger(uint[] words)
    {
        var value = BigInteger.Zero;
        for (var i = words.Length - 1; i >= 0; i--)
        {
            value = (value << 32) | words[i];
        }

        return value;
    }

    // The low count words of value
    public static uint[] ToWords(BigInteger value, int count)
    {
        var words = new uint[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = (uint)(value & uint.MaxValue);
            value >>= 32;
        }

        return words;
    }
}
