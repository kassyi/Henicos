using System;

namespace Henicos.PiBenchmark;

/// <summary>
/// Provides verification functionality for computed Pi digits.
/// </summary>
public static class PiVerification
{
    /// <summary>
    /// Verifies the trailing hexadecimal digits of the computed Pi against the BBP algorithm.
    /// </summary>
    /// <param name="computedHexTail">The trailing hex digits computed from the GPU result.</param>
    /// <param name="offset">The 0-based index of the first digit in <paramref name="computedHexTail"/>.</param>
    /// <returns>True if verification passes, otherwise false.</returns>
    public static bool VerifyWithBbp(string computedHexTail, int offset)
    {
        if (string.IsNullOrEmpty(computedHexTail))
        {
            Console.WriteLine("[Verification] FAIL: Empty computed string provided.");
            return false;
        }

        int count = computedHexTail.Length;
        Console.WriteLine($"[Verification] Extracting {count} hex digits at offset {offset} via BBP...");
        
        string expectedHex = BbpDigitExtractor.ExtractHexDigits(offset, count);
        expectedHex = expectedHex.ToUpperInvariant();
        computedHexTail = computedHexTail.ToUpperInvariant();

        if (computedHexTail == expectedHex)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Verification] PASS: Expected {expectedHex} == Computed {computedHexTail}");
            Console.ResetColor();
            return true;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Verification] FAIL: Expected {expectedHex} != Computed {computedHexTail}");
            Console.ResetColor();
            return false;
        }
    }
}
