using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Metadata header identifying the modulus <c>N(E) = 2^(2E+1) - 2^E + 11</c>, base <c>a</c>, Pietrzak halving depth
/// <c>k</c> and canonical residue word count of a <see cref="FermatPrpProof"/>.
/// </summary>
/// <param name="E">The exponent <c>E</c> of <c>N = 2^(2E+1) - 2^E + 11</c>.</param>
/// <param name="Base">The base <c>a</c>.</param>
/// <param name="ProofPower">The halving tree depth <c>k</c> (<c>2^k</c> leaves per segment).</param>
/// <param name="ResidueWords">The canonical word count <c>ceil((2E + 1) / 32)</c> of each residue.</param>
public readonly record struct FermatPrpProofHeader(int E, uint Base, int ProofPower, int ResidueWords)
{
    /// <summary>
    /// Initializes a proof header deriving <see cref="ResidueWords"/> from <paramref name="e"/> when in range.
    /// </summary>
    /// <param name="e">The exponent <c>E</c>.</param>
    /// <param name="base">The base <c>a</c>.</param>
    /// <param name="proofPower">The halving tree depth <c>k</c>.</param>
    public FermatPrpProofHeader(int e, uint @base, int proofPower)
        : this(
            e,
            @base,
            proofPower,
            e >= QuadraticFoldModulus.MinExponent && e <= QuadraticFoldModulus.MaxExponent
                ? QuadraticFoldModulus.WordsOf(e)
                : 0)
    {
    }
}

/// <summary>
/// Dual-segment batched Pietrzak proof certifying that a squaring chain modulo <c>N(E) = 2^(2E+1) - 2^E + 11</c>
/// produced <see cref="ResidueE"/> (<c>u_E = a^(2^E) mod N</c>) and <see cref="ResidueK"/>
/// (<c>u_K = a^(2^(2E+1)) mod N</c>) from base <see cref="Base"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both segments <c>[r_0, E]</c> and <c>[E + 1 + r_0, 2E + 1]</c> (<c>r_0 = E mod 2^k</c>) have length
/// <c>2^k * d</c> (<c>d = floor(E / 2^k)</c>), so a 128-bit odd Fiat-Shamir challenge merges them into a single binary
/// halving tree of <c>k = ProofPower</c> witnesses (<see cref="Witnesses"/>) verified by
/// <see cref="FermatAlgorithm.VerifyProof"/> with a single final <c>d</c>-squaring chain.
/// </para>
/// <para>Instances are immutable; every residue buffer is defensively copied on construction.</para>
/// </remarks>
public sealed class FermatPrpProof
{
    /// <summary>The largest supported <see cref="ProofPower"/> (<c>2^20 = 1,048,576</c> leaves per segment).</summary>
    public const int MaxProofPower = 20;

    private const int FormatVersion = 1;
    private const int HeaderBytes = 24;
    private const int HashBytes = 32;

    private static ReadOnlySpan<byte> Magic => "GPRP"u8;

    /// <summary>Initializes an immutable Pietrzak proof.</summary>
    /// <param name="header">The modulus, base, halving depth and residue word count.</param>
    /// <param name="residueE">The canonical residue <c>u_E = a^(2^E) mod N</c>.</param>
    /// <param name="residueK">The canonical residue <c>u_K = a^(2^(2E+1)) mod N</c>.</param>
    /// <param name="witnesses">The <c>k</c> halving witnesses <c>mu_0, ..., mu_(k-1)</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="witnesses"/> is <see langword="null"/>.</exception>
    public FermatPrpProof(
        FermatPrpProofHeader header,
        ReadOnlyMemory<uint> residueE,
        ReadOnlyMemory<uint> residueK,
        IReadOnlyList<ReadOnlyMemory<uint>> witnesses)
    {
        ArgumentNullException.ThrowIfNull(witnesses);

        Header = header;
        ResidueE = residueE.ToArray();
        ResidueK = residueK.ToArray();

        var copiedWitnesses = new ReadOnlyMemory<uint>[witnesses.Count];
        for (var i = 0; i < witnesses.Count; i++)
        {
            copiedWitnesses[i] = witnesses[i].ToArray();
        }

        Witnesses = Array.AsReadOnly(copiedWitnesses);
    }

    /// <summary>Gets the proof metadata header.</summary>
    public FermatPrpProofHeader Header { get; }

    /// <summary>Gets the exponent <c>E</c> of <c>N = 2^(2E+1) - 2^E + 11</c>.</summary>
    public int E => Header.E;

    /// <summary>Gets the base <c>a</c>.</summary>
    public uint Base => Header.Base;

    /// <summary>Gets the halving tree depth <c>k</c> (<c>2^k</c> leaves per segment).</summary>
    public int ProofPower => Header.ProofPower;

    /// <summary>Gets the canonical word count <c>ceil((2E + 1) / 32)</c> of each residue.</summary>
    public int ResidueWords => Header.ResidueWords;

    /// <summary>Gets the canonical residue <c>u_E = a^(2^E) mod N</c>.</summary>
    public ReadOnlyMemory<uint> ResidueE { get; }

    /// <summary>Gets the canonical residue <c>u_K = a^(2^(2E+1)) mod N</c> (<c>K = 2E + 1</c>).</summary>
    public ReadOnlyMemory<uint> ResidueK { get; }

    /// <summary>Gets the <c>k</c> canonical halving witnesses <c>mu_0, ..., mu_(k-1)</c>.</summary>
    public IReadOnlyList<ReadOnlyMemory<uint>> Witnesses { get; }

    /// <summary>
    /// Atomically writes the proof to <paramref name="path"/> (via a temporary file and rename) with a trailing SHA-256
    /// integrity digest.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/>, empty or whitespace.</exception>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{fullPath}.tmp";
        Span<byte> headerBuffer = stackalloc byte[HeaderBytes];
        Magic.CopyTo(headerBuffer);
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer[4..8], FormatVersion);
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer[8..12], E);
        BinaryPrimitives.WriteUInt32LittleEndian(headerBuffer[12..16], Base);
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer[16..20], ProofPower);
        BinaryPrimitives.WriteInt32LittleEndian(headerBuffer[20..24], ResidueWords);

        Span<byte> digest = stackalloc byte[HashBytes];
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            WriteAndHash(stream, sha, headerBuffer);
            WriteAndHash(stream, sha, MemoryMarshal.AsBytes(ResidueE.Span));
            WriteAndHash(stream, sha, MemoryMarshal.AsBytes(ResidueK.Span));
            for (var i = 0; i < Witnesses.Count; i++)
            {
                WriteAndHash(stream, sha, MemoryMarshal.AsBytes(Witnesses[i].Span));
            }

            sha.GetHashAndReset(digest);
            stream.Write(digest);
            stream.Flush();
        }

        File.Move(tempPath, fullPath, overwrite: true);
    }

    /// <summary>Loads a <see cref="FermatPrpProof"/> from <paramref name="path"/> and verifies its SHA-256 integrity hash.</summary>
    /// <param name="path">The proof file path.</param>
    /// <returns>The loaded <see cref="FermatPrpProof"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/>, empty or whitespace.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file header, length or SHA-256 digest is invalid.</exception>
    public static FermatPrpProof Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Pietrzak proof file not found: {path}", path);
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < HeaderBytes + HashBytes)
        {
            throw new InvalidDataException("The Pietrzak proof file is corrupted or truncated.");
        }

        Span<byte> headerBuffer = stackalloc byte[HeaderBytes];
        stream.ReadExactly(headerBuffer);
        if (!headerBuffer[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("The file is not in the GPRP Pietrzak proof format.");
        }

        var version = BinaryPrimitives.ReadInt32LittleEndian(headerBuffer[4..8]);
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Unsupported Pietrzak proof format version {version}.");
        }

        var header = new FermatPrpProofHeader(
            BinaryPrimitives.ReadInt32LittleEndian(headerBuffer[8..12]),
            BinaryPrimitives.ReadUInt32LittleEndian(headerBuffer[12..16]),
            BinaryPrimitives.ReadInt32LittleEndian(headerBuffer[16..20]),
            BinaryPrimitives.ReadInt32LittleEndian(headerBuffer[20..24]));

        if (header.ProofPower < 1 || header.ProofPower > MaxProofPower || header.ResidueWords <= 0)
        {
            throw new InvalidDataException("The Pietrzak proof file header contains invalid dimensions.");
        }

        var residueBytes = (long)header.ResidueWords * sizeof(uint);
        var expectedLength = HeaderBytes + (2L + header.ProofPower) * residueBytes + HashBytes;
        if (stream.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"The Pietrzak proof file length ({stream.Length} bytes) does not match expected {expectedLength} bytes.");
        }

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(headerBuffer);

        var residueE = ReadAndHashWords(stream, sha, header.ResidueWords);
        var residueK = ReadAndHashWords(stream, sha, header.ResidueWords);
        var witnesses = new ReadOnlyMemory<uint>[header.ProofPower];
        for (var i = 0; i < header.ProofPower; i++)
        {
            witnesses[i] = ReadAndHashWords(stream, sha, header.ResidueWords);
        }

        Span<byte> expectedHash = stackalloc byte[HashBytes];
        Span<byte> actualHash = stackalloc byte[HashBytes];
        stream.ReadExactly(expectedHash);
        sha.GetHashAndReset(actualHash);

        if (!actualHash.SequenceEqual(expectedHash))
        {
            throw new InvalidDataException("Pietrzak proof file SHA-256 hash mismatch; the file is corrupted.");
        }

        return new FermatPrpProof(header, residueE, residueK, witnesses);
    }

    private static void WriteAndHash(FileStream stream, IncrementalHash sha, ReadOnlySpan<byte> bytes)
    {
        stream.Write(bytes);
        sha.AppendData(bytes);
    }

    private static uint[] ReadAndHashWords(FileStream stream, IncrementalHash sha, int wordCount)
    {
        var words = new uint[wordCount];
        var bytes = MemoryMarshal.AsBytes(words.AsSpan());
        stream.ReadExactly(bytes);
        sha.AppendData(bytes);
        return words;
    }
}
