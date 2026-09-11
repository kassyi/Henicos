namespace Kassyi.GSharp.Tests.Helpers;

public static class HashHelper
{
    /// <summary>
    /// Computes a short (10-character) hexadecimal hash using the 64-bit FNV-1a algorithm.
    /// This is deterministic, fast, zero-allocation, and cross-platform.
    /// </summary>
    public static string ComputeShortHash(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16")[..10];
    }
}
