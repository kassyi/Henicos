namespace Kassyi.GSharp.Hashing;

/// <summary>
/// The 64-bit FNV-1a hash core (offset basis <c>14695981039346656037</c>, prime <c>1099511628211</c>), fed one
/// UTF-16 code unit or one 32-bit value at a time. Callers decide how their input is fed (whole string, CRLF skipped,
/// a line number mixed in) and how the result is written (full hex, shortened); only the mixing step lives here.
/// </summary>
/// <remarks>
/// The results are persisted (snapshot file names, <c>*.gsym.json</c> source hashes, shared-memory site keys), so the
/// arithmetic must never change. Linked into every project that needs it (netstandard2.0 compatible).
/// Create it with <c>new Fnv1A64()</c>; <c>default</c> would start from zero instead of the offset basis.
/// </remarks>
internal struct Fnv1A64
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _hash;

    /// <summary>Starts a hash at the FNV offset basis.</summary>
    public Fnv1A64()
    {
        _hash = OffsetBasis;
    }

    /// <summary>Gets the hash of everything appended so far.</summary>
    public readonly ulong Value => _hash;

    /// <summary>Mixes in one UTF-16 code unit (not a UTF-8 byte).</summary>
    public void Append(char value) => Append((uint)value);

    /// <summary>Mixes in every UTF-16 code unit of <paramref name="value"/>.</summary>
    public void Append(string value)
    {
        foreach (var c in value)
        {
            Append((uint)c);
        }
    }

    /// <summary>Mixes in a 32-bit value in one step (one xor, one multiply), not as four bytes.</summary>
    public void Append(uint value)
    {
        unchecked
        {
            _hash ^= value;
            _hash *= Prime;
        }
    }

    /// <summary>Computes the hash of a whole string.</summary>
    public static ulong Compute(string text)
    {
        var hash = new Fnv1A64();
        hash.Append(text);
        return hash.Value;
    }
}
