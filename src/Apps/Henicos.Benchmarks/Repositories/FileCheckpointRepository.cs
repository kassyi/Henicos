using System.Security.Cryptography;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Henicos.Benchmarks.Repositories;

public interface ICheckpointRepository
{
    bool Exists();
    FermatPrpCheckpoint Load();
    void Save(FermatPrpCheckpoint checkpoint);
}

/// <summary>
/// Stores a <see cref="FermatPrpCheckpoint"/> in a file with a SHA-256 hash, written through a temporary file and a move.
/// </summary>
/// <remarks>
/// Layout (little-endian): the magic <c>GFP2</c>, <c>E</c> (int), base (uint), block size (int), iteration (long),
/// checks passed (int), rollbacks (int), then the state, the check, <c>u_E</c> and <c>u_K</c>, each as a word count
/// (int) followed by the words, and finally the SHA-256 of everything before it. Files of the earlier format (an int
/// iteration and the raw state words) have no magic and are rejected.
/// </remarks>
public class FileCheckpointRepository(string checkpointFile, string tempCheckpointFile) : ICheckpointRepository
{
    private static ReadOnlySpan<byte> Magic => "GFP2"u8;

    public bool Exists() => File.Exists(checkpointFile);

    public FermatPrpCheckpoint Load()
    {
        if (!Exists())
        {
            throw new FileNotFoundException($"Checkpoint file not found: {checkpointFile}");
        }

        var bytes = File.ReadAllBytes(checkpointFile);
        if (bytes.Length < Magic.Length || !bytes.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new InvalidDataException(
                "The checkpoint file is not in the FermatPrpCheckpoint format (an earlier format, or another file); it cannot be resumed.");
        }

        if (bytes.Length < Magic.Length + 32)
        {
            throw new InvalidDataException("Checkpoint file is corrupted or truncated.");
        }

        var payload = bytes.AsSpan(0, bytes.Length - 32);
        if (!SHA256.HashData(payload).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - 32)))
        {
            throw new InvalidDataException("Checkpoint file hash mismatch! Data is corrupted.");
        }

        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes, Magic.Length, payload.Length - Magic.Length));
            var e = reader.ReadInt32();
            var @base = reader.ReadUInt32();
            var blockSize = reader.ReadInt32();
            var iteration = reader.ReadInt64();
            var checksPassed = reader.ReadInt32();
            var rollbacks = reader.ReadInt32();
            var state = ReadWords(reader);
            var check = ReadWords(reader);
            var snapshotE = ReadWords(reader);
            var finalK = ReadWords(reader);
            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("Checkpoint file has trailing data.");
            }

            return new FermatPrpCheckpoint(e, @base, blockSize, iteration, state, check, snapshotE, finalK,
                checksPassed, rollbacks);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("Checkpoint file is corrupted or truncated.", ex);
        }
    }

    public void Save(FermatPrpCheckpoint checkpoint)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(checkpoint.E);
                writer.Write(checkpoint.Base);
                writer.Write(checkpoint.BlockSize);
                writer.Write(checkpoint.Iteration);
                writer.Write(checkpoint.ChecksPassed);
                writer.Write(checkpoint.Rollbacks);
                WriteWords(writer, checkpoint.State.Span);
                WriteWords(writer, checkpoint.Check.Span);
                WriteWords(writer, checkpoint.SnapshotE.Span);
                WriteWords(writer, checkpoint.FinalK.Span);
            }

            var payload = stream.ToArray();
            using var file = File.Create(tempCheckpointFile);
            file.Write(payload);
            file.Write(SHA256.HashData(payload));
        }

        File.Move(tempCheckpointFile, checkpointFile, overwrite: true);
    }

    private static uint[] ReadWords(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || (long)count * 4 > reader.BaseStream.Length - reader.BaseStream.Position)
        {
            throw new InvalidDataException("Checkpoint file is corrupted or truncated.");
        }

        var words = new uint[count];
        var bytes = MemoryMarshal.AsBytes(words.AsSpan());
        reader.BaseStream.ReadExactly(bytes);
        return words;
    }

    private static void WriteWords(BinaryWriter writer, ReadOnlySpan<uint> words)
    {
        writer.Write(words.Length);
        writer.Write(MemoryMarshal.AsBytes(words));
    }
}
