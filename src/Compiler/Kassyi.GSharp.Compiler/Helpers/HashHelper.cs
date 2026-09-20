namespace Kassyi.GSharp.Compiler.Helpers;

internal static class HashHelper
{
    /// <summary>
    /// Computes a stable FNV-1a 32-bit hash for a string, ensuring determinism across AppDomains/executions.
    /// This is crucial for Roslyn Incremental Generators which rely on stable HashCodes for caching.
    /// </summary>
    public static int ComputeFnv1AHash(string? text)
    {
        if (text is null or { Length: 0 })
        {
            return 0;
        }

        unchecked
        {
            var hash = (int)2166136261;
            foreach (var c in text)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }

    /// <summary>
    /// Combines multiple object hashes into a single deterministic hash.
    /// If an object is a string, it uses ComputeFnv1AHash for stability.
    /// </summary>
    public static int Combine(params object?[] values)
    {
        unchecked
        {
            var hash = 17;
            foreach (var value in values)
            {
                if (value is string s)
                {
                    hash = hash * 31 + ComputeFnv1AHash(s);
                }
                else
                {
                    hash = hash * 31 + (value?.GetHashCode() ?? 0);
                }
            }
            return hash;
        }
    }
}
