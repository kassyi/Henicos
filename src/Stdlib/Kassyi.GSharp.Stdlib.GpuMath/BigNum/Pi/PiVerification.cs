namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Pi;

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
            return false;
        }

        var count = computedHexTail.Length;

        var expectedHex = BbpAlgorithm.ExtractHexDigits(offset, count);
        expectedHex = expectedHex.ToUpperInvariant();
        computedHexTail = computedHexTail.ToUpperInvariant();

        if (computedHexTail == expectedHex)
        {
            Logger.Info("Verification", $"PASS: Expected {expectedHex} == Computed {computedHexTail}");
            return true;
        }
        else
        {
            Logger.Error("Verification", $"FAIL: Expected {expectedHex} != Computed {computedHexTail}");
            return false;
        }
    }
}
