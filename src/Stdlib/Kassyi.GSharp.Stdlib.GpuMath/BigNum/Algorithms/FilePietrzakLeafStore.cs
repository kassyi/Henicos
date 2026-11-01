using System.Buffers.Binary;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Seekable disk-backed <see cref="IPietrzakLeafStore"/> keeping host RAM usage <c>O(ResidueWords)</c> during multi-day
/// 43M-digit runs and persisting the recorded-leaf bitmap across process restarts with <see cref="FermatAlgorithm.Restore"/>.
/// </summary>
internal sealed class FilePietrzakLeafStore : IPietrzakLeafStore
{
    private const int FormatVersion = 1;
    private const int FixedHeaderBytes = 24;

    private static ReadOnlySpan<byte> Magic => "GPLF"u8;

    private readonly FileStream _stream;
    private readonly byte[] _bitmap;
    private readonly int _headerBytes;
    private readonly long _slotBytes;
    private readonly int _totalStorageSlots;

    public FilePietrzakLeafStore(string path, PietrzakSchedule schedule)
    {
        TotalLeafSlots = schedule.TotalLeafSlots;
        ResidueWords = schedule.ResidueWords;
        _totalStorageSlots = schedule.TotalStorageSlots;
        _bitmap = new byte[(TotalLeafSlots + 7) / 8];
        _headerBytes = FixedHeaderBytes + _bitmap.Length;
        _slotBytes = (long)ResidueWords * sizeof(uint);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _stream = new FileStream(fullPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            InitializeOrAttachHeader(schedule.Header);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    public int TotalLeafSlots { get; }

    public int ResidueWords { get; }

    public bool HasLeafSlot(int leafSlot) =>
        (_bitmap[leafSlot >> 3] & (1 << (leafSlot & 7))) != 0;

    public void ResetLeaves()
    {
        Array.Clear(_bitmap);
        WriteBitmap();
    }

    public void WriteSlot(int slot, ReadOnlySpan<uint> words)
    {
        if ((uint)slot >= (uint)_totalStorageSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        if (words.Length != ResidueWords)
        {
            throw new ArgumentException($"Expected {ResidueWords} words, got {words.Length}.", nameof(words));
        }

        _stream.Seek(_headerBytes + slot * _slotBytes, SeekOrigin.Begin);
        _stream.Write(MemoryMarshal.AsBytes(words));

        if (slot < TotalLeafSlots)
        {
            var byteIndex = slot >> 3;
            var mask = (byte)(1 << (slot & 7));
            if ((_bitmap[byteIndex] & mask) == 0)
            {
                _bitmap[byteIndex] |= mask;
                _stream.Seek(FixedHeaderBytes + byteIndex, SeekOrigin.Begin);
                _stream.WriteByte(_bitmap[byteIndex]);
            }
        }
    }

    public void ReadSlot(int slot, Span<uint> destination)
    {
        if ((uint)slot >= (uint)_totalStorageSlots)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        if (destination.Length != ResidueWords)
        {
            throw new ArgumentException($"Expected {ResidueWords} words, got {destination.Length}.", nameof(destination));
        }

        _stream.Seek(_headerBytes + slot * _slotBytes, SeekOrigin.Begin);
        _stream.ReadExactly(MemoryMarshal.AsBytes(destination));
    }

    public void Flush() => _stream.Flush();

    public void Dispose()
    {
        if (_stream != null)
        {
            _stream.Dispose();
        }
    }

    private void InitializeOrAttachHeader(FermatPrpProofHeader header)
    {
        if (_stream.Length == 0)
        {
            Span<byte> buffer = stackalloc byte[FixedHeaderBytes];
            Magic.CopyTo(buffer);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[4..8], FormatVersion);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[8..12], header.E);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer[12..16], header.Base);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[16..20], header.ProofPower);
            BinaryPrimitives.WriteInt32LittleEndian(buffer[20..24], header.ResidueWords);

            _stream.Seek(0, SeekOrigin.Begin);
            _stream.Write(buffer);
            _stream.Write(_bitmap);
            _stream.Flush();
            return;
        }

        if (_stream.Length < _headerBytes)
        {
            throw new InvalidOperationException("The existing Pietrzak leaf store file is truncated.");
        }

        Span<byte> existing = stackalloc byte[FixedHeaderBytes];
        _stream.Seek(0, SeekOrigin.Begin);
        _stream.ReadExactly(existing);
        var fileHeader = new FermatPrpProofHeader(
            BinaryPrimitives.ReadInt32LittleEndian(existing[8..12]),
            BinaryPrimitives.ReadUInt32LittleEndian(existing[12..16]),
            BinaryPrimitives.ReadInt32LittleEndian(existing[16..20]),
            BinaryPrimitives.ReadInt32LittleEndian(existing[20..24]));

        if (!existing[..4].SequenceEqual(Magic) ||
            BinaryPrimitives.ReadInt32LittleEndian(existing[4..8]) != FormatVersion ||
            fileHeader != header)
        {
            throw new InvalidOperationException(
                "The existing Pietrzak leaf store file belongs to another (E, Base, ProofPower) configuration.");
        }

        _stream.ReadExactly(_bitmap);
    }

    private void WriteBitmap()
    {
        _stream.Seek(FixedHeaderBytes, SeekOrigin.Begin);
        _stream.Write(_bitmap);
    }
}

