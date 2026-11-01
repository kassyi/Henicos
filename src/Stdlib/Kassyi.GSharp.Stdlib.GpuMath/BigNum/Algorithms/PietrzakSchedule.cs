using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates the proof header, leaf positions, step lengths and domain-separated Fiat-Shamir challenge derivation
/// for the dual-segment Pietrzak tree over <c>[r_0, E]</c> and <c>[E + 1 + r_0, 2E + 1]</c>.
/// </summary>
internal readonly struct PietrzakSchedule
{
    private static ReadOnlySpan<byte> MergeDomainTag => "gsharp-prp-merge-v1"u8;

    private static ReadOnlySpan<byte> RoundDomainTag => "gsharp-prp-round-v1"u8;

    public PietrzakSchedule(FermatPrpProofHeader header)
    {
        Header = header;
        SegmentLeafCount = 1 << header.ProofPower;
        StepSize = header.E >> header.ProofPower;
        Remainder = header.E & (SegmentLeafCount - 1);
        SegmentBStart = header.E + 1L + Remainder;
    }

    public FermatPrpProofHeader Header { get; }

    public int E => Header.E;

    public uint Base => Header.Base;

    public int ProofPower => Header.ProofPower;

    public int ResidueWords => Header.ResidueWords;

    public int SegmentLeafCount { get; }

    public int TotalLeafSlots => SegmentLeafCount * 2;

    public int StepSize { get; }

    public int Remainder { get; }

    public long SegmentBStart { get; }

    public long GetLeafIteration(int leafSlot) =>
        leafSlot < SegmentLeafCount
            ? Remainder + (long)leafSlot * StepSize
            : SegmentBStart + (long)(leafSlot - SegmentLeafCount) * StepSize;

    public bool TryGetLeafSlot(long iteration, out int leafSlot)
    {
        if (iteration >= Remainder && iteration < E)
        {
            var offset = iteration - Remainder;
            if (offset % StepSize == 0)
            {
                leafSlot = (int)(offset / StepSize);
                return true;
            }
        }
        else if (iteration >= SegmentBStart && iteration < 2L * E + 1)
        {
            var offset = iteration - SegmentBStart;
            if (offset % StepSize == 0)
            {
                leafSlot = SegmentLeafCount + (int)(offset / StepSize);
                return true;
            }
        }

        leafSlot = -1;
        return false;
    }

    public long NextLeafAfter(long iteration)
    {
        if (iteration < Remainder)
        {
            return Remainder;
        }

        var lastLeafA = E - (long)StepSize;
        if (iteration < lastLeafA)
        {
            var nextIndex = (iteration - Remainder) / StepSize + 1;
            return Remainder + nextIndex * StepSize;
        }

        if (iteration < SegmentBStart)
        {
            return SegmentBStart;
        }

        var lastLeafB = 2L * E + 1 - StepSize;
        if (iteration < lastLeafB)
        {
            var nextIndex = (iteration - SegmentBStart) / StepSize + 1;
            return SegmentBStart + nextIndex * StepSize;
        }

        return long.MaxValue;
    }

    public UInt128 DeriveMergeChallenge(ReadOnlySpan<uint> residueE, ReadOnlySpan<uint> residueK)
    {
        Span<byte> meta = stackalloc byte[16];
        WriteHeaderBytes(meta);

        Span<byte> digest = stackalloc byte[32];
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(MergeDomainTag);
        sha.AppendData(meta);
        sha.AppendData(MemoryMarshal.AsBytes(residueE));
        sha.AppendData(MemoryMarshal.AsBytes(residueK));
        sha.GetHashAndReset(digest);

        return ForceMsbAndOdd(BinaryPrimitives.ReadUInt128LittleEndian(digest[..16]));
    }

    public UInt128 DeriveRoundChallenge(int round, PietrzakClaim claim, ReadOnlySpan<uint> witness)
    {
        Span<byte> meta = stackalloc byte[20];
        WriteHeaderBytes(meta[..16]);
        BinaryPrimitives.WriteInt32LittleEndian(meta[16..20], round);

        Span<byte> digest = stackalloc byte[32];
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(RoundDomainTag);
        sha.AppendData(meta);
        sha.AppendData(MemoryMarshal.AsBytes(claim.X.AsSpan()));
        sha.AppendData(MemoryMarshal.AsBytes(claim.Y.AsSpan()));
        sha.AppendData(MemoryMarshal.AsBytes(witness));
        sha.GetHashAndReset(digest);

        return ForceMsbAndOdd(BinaryPrimitives.ReadUInt128LittleEndian(digest[..16]));
    }

    private void WriteHeaderBytes(Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination[..4], Header.E);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..8], Header.Base);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..12], Header.ProofPower);
        BinaryPrimitives.WriteInt32LittleEndian(destination[12..16], Header.ResidueWords);
    }

    private static UInt128 ForceMsbAndOdd(UInt128 raw) =>
        raw | (UInt128.One << 127) | UInt128.One;
}
