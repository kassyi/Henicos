using System.Buffers.Binary;

namespace Kassyi.GSharp.Runtime.Fatbin;

/// <summary>
/// Provides defensive parsing and inspection for NVIDIA FATBIN binaries (.fatbin, .gfatbin).
/// </summary>
public static class FatbinParser
{
    /// <summary>
    /// Recommended file extension for G#-signed fatbins.
    /// </summary>
    public const string GSharpFatbinExtension = ".gfatbin";

    /// <summary>
    /// Standard NVIDIA FATBIN ELF magic number (0xBA55ED50).
    /// </summary>
    private const uint FatbinMagic = 0xBA55ED50;

    /// <summary>
    /// Minimum header size of a FATBIN ELF binary (16 bytes).
    /// </summary>
    private const int MinimumHeaderSize = 16;

    /// <summary>
    /// Minimum size of an item header required to extract architecture and name bounds (40 bytes).
    /// </summary>
    private const int MinimumItemHeaderSize = 40;

    /// <summary>
    /// Size of the G# appended SHA-256 signature in bytes.
    /// </summary>
    private const int GSharpSignatureSize = 32;

    /// <summary>
    /// Attempts to defensively parse a fatbin binary span.
    /// Returns false gracefully if headers do not match expected magic or bounds without throwing exceptions.
    /// </summary>
    /// <param name="binary">The byte span containing the fatbin binary data.</param>
    /// <param name="fatbin">When this method returns, contains the parsed <see cref="FatbinImage"/> if successful; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<byte> binary, [NotNullWhen(true)] out FatbinImage? fatbin) =>
        TryParseInternal(binary, ReadOnlyMemory<byte>.Empty, isMemoryBacked: false, out fatbin);

    /// <summary>
    /// Attempts to defensively parse a fatbin binary memory buffer.
    /// Returns false gracefully if headers do not match expected magic or bounds without throwing exceptions.
    /// </summary>
    /// <param name="binary">The memory buffer containing the fatbin binary data.</param>
    /// <param name="fatbin">When this method returns, contains the parsed <see cref="FatbinImage"/> if successful; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlyMemory<byte> binary, [NotNullWhen(true)] out FatbinImage? fatbin) =>
        TryParseInternal(binary.Span, binary, isMemoryBacked: true, out fatbin);

    /// <summary>
    /// Attempts to defensively parse a fatbin binary byte array.
    /// </summary>
    /// <param name="binary">The byte array containing the fatbin binary data.</param>
    /// <param name="fatbin">When this method returns, contains the parsed <see cref="FatbinImage"/> if successful; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(byte[]? binary, [NotNullWhen(true)] out FatbinImage? fatbin)
    {
        if (binary is not null)
        {
            return TryParse((ReadOnlyMemory<byte>)binary, out fatbin);
        }

        fatbin = null;
        return false;

    }

    private static bool TryParseInternal(
        ReadOnlySpan<byte> span,
        ReadOnlyMemory<byte> memory,
        bool isMemoryBacked,
        [NotNullWhen(true)] out FatbinImage? fatbin)
    {
        fatbin = null;

        if (!TryParseHeader(span, out var header))
        {
            return false;
        }

        var totalPayloadSize = header.HeaderSize + header.PayloadItemsSize;

        // Defensive check against overflow or binary shorter than the declared payload
        if ((ulong)span.Length < totalPayloadSize || totalPayloadSize < header.HeaderSize)
        {
            return false;
        }

        var hasGSharpSignature = (ulong)span.Length == totalPayloadSize + GSharpSignatureSize;
        var (payloadMemory, signatureMemory) = ResolveBuffers(span, memory, isMemoryBacked, (int)totalPayloadSize, hasGSharpSignature);

        if (!TryParseItems(span, header.HeaderSize, (int)totalPayloadSize, out var items))
        {
            return false;
        }

        fatbin = new FatbinImage(
            header.Magic,
            header.Version,
            totalPayloadSize,
            hasGSharpSignature,
            payloadMemory,
            signatureMemory,
            items.AsReadOnly());

        return true;
    }

    private static bool TryParseHeader(ReadOnlySpan<byte> span, out FatbinHeader header)
    {
        header = default;

        if (span.Length < MinimumHeaderSize)
        {
            return false;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4));
        if (magic != FatbinMagic)
        {
            return false;
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4, 2));
        var headerSize = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6, 2));
        if (headerSize < MinimumHeaderSize)
        {
            return false;
        }

        var payloadItemsSize = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(8, 8));
        header = new FatbinHeader(magic, version, headerSize, payloadItemsSize);
        return true;
    }

    private static (ReadOnlyMemory<byte> Payload, ReadOnlyMemory<byte> Signature) ResolveBuffers(
        ReadOnlySpan<byte> span,
        ReadOnlyMemory<byte> memory,
        bool isMemoryBacked,
        int totalPayloadSize,
        bool hasGSharpSignature)
    {
        if (isMemoryBacked)
        {
            var payload = memory[..totalPayloadSize];
            var signature = hasGSharpSignature
                ? memory.Slice(totalPayloadSize, GSharpSignatureSize)
                : ReadOnlyMemory<byte>.Empty;
            return (payload, signature);
        }

        var unmanagedPayload = span[..totalPayloadSize].ToArray();
        var unmanagedSignature = hasGSharpSignature
            ? span.Slice(totalPayloadSize, GSharpSignatureSize).ToArray()
            : ReadOnlyMemory<byte>.Empty;
        return (unmanagedPayload, unmanagedSignature);
    }

    private static bool TryParseItems(
        ReadOnlySpan<byte> span,
        int offset,
        int payloadEnd,
        [NotNullWhen(true)] out List<FatbinItemInfo>? items)
    {
        items = null;
        var result = new List<FatbinItemInfo>();

        while (offset < payloadEnd)
        {
            if (!TryParseItem(span.Slice(offset), payloadEnd - offset, out var item, out var totalItemSize))
            {
                return false;
            }

            result.Add(item);
            offset += totalItemSize;
        }

        items = result;
        return true;
    }

    private static bool TryParseItem(
        ReadOnlySpan<byte> itemSpan,
        int remainingBytes,
        out FatbinItemInfo item,
        out int totalItemSize)
    {
        item = default;
        totalItemSize = 0;

        if (remainingBytes < MinimumItemHeaderSize)
        {
            return false;
        }

        var type = BinaryPrimitives.ReadUInt16LittleEndian(itemSpan.Slice(0x00, 2));
        var itemHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(itemSpan.Slice(0x04, 4));
        var itemDataSize = BinaryPrimitives.ReadUInt64LittleEndian(itemSpan.Slice(0x08, 8));

        if (itemHeaderSize < MinimumItemHeaderSize)
        {
            return false;
        }

        var itemSize = itemHeaderSize + itemDataSize;
        if (itemSize == 0 || (ulong)remainingBytes < itemSize)
        {
            return false;
        }

        var rawMinor = BinaryPrimitives.ReadUInt16LittleEndian(itemSpan.Slice(0x18, 2));
        var rawMajor = BinaryPrimitives.ReadUInt16LittleEndian(itemSpan.Slice(0x1A, 2));
        var computeVersion = BinaryPrimitives.ReadUInt32LittleEndian(itemSpan.Slice(0x1C, 4));

        var (major, minor) = computeVersion >= 10
            ? ((ushort)(computeVersion / 10), (ushort)(computeVersion % 10))
            : (rawMajor, rawMinor);

        var nameOff = BinaryPrimitives.ReadUInt32LittleEndian(itemSpan.Slice(0x20, 4));
        var nameLen = BinaryPrimitives.ReadUInt32LittleEndian(itemSpan.Slice(0x24, 4));
        var objectName = TryExtractObjectName(itemSpan, nameOff, nameLen, itemHeaderSize);

        item = new FatbinItemInfo(type, major, minor, computeVersion, objectName, itemDataSize);
        totalItemSize = (int)itemSize;
        return true;
    }

    private static string? TryExtractObjectName(
        ReadOnlySpan<byte> itemSpan,
        uint nameOff,
        uint nameLen,
        uint itemHeaderSize)
    {
        if (nameLen == 0 || nameOff == 0 || nameOff + nameLen > itemHeaderSize)
        {
            return null;
        }

        var nameBytes = itemSpan.Slice((int)nameOff, (int)nameLen);
        var nullIdx = nameBytes.IndexOf((byte)0);
        if (nullIdx >= 0)
        {
            nameBytes = nameBytes[..nullIdx];
        }

        return Encoding.UTF8.GetString(nameBytes);
    }

    private readonly record struct FatbinHeader(
        uint Magic,
        ushort Version,
        ushort HeaderSize,
        ulong PayloadItemsSize);
}
