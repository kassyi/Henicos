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
}
