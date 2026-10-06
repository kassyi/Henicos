using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Henicos.Benchmarks.Repositories;

public interface ICheckpointRepository
{
    bool Exists();
    (int Iteration, uint[] State) Load(int outNumWords);
    void Save(int iteration, uint[] state);
}

public class FileCheckpointRepository(string checkpointFile, string tempCheckpointFile) : ICheckpointRepository
{
    public bool Exists() => File.Exists(checkpointFile);

    public (int Iteration, uint[] State) Load(int outNumWords)
    {
        if (!Exists())
        {
            throw new FileNotFoundException($"Checkpoint file not found: {checkpointFile}");
        }

        using var fs = File.OpenRead(checkpointFile);
        using var br = new BinaryReader(fs);

        var startIter = br.ReadInt32();

        var state = new uint[outNumWords];
        var bytes = MemoryMarshal.AsBytes(state.AsSpan());

        if (fs.Read(bytes) != bytes.Length)
        {
            throw new InvalidDataException("Checkpoint file is corrupted or truncated.");
        }

        var storedHash = br.ReadBytes(32);
        if (storedHash.Length != 32)
        {
            throw new InvalidDataException("Checkpoint file is missing SHA256 hash or truncated.");
        }

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(BitConverter.GetBytes(startIter));
        hasher.AppendData(bytes);
        var computedHash = hasher.GetHashAndReset();

        return !computedHash.SequenceEqual(storedHash)
            ? throw new InvalidDataException("Checkpoint file hash mismatch! Data is corrupted.")
            : (startIter, state);
    }

    public void Save(int iteration, uint[] state)
    {
        using (var fs = File.Create(tempCheckpointFile))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(iteration);
            var bytes = MemoryMarshal.AsBytes(state.AsSpan());
            fs.Write(bytes);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hasher.AppendData(BitConverter.GetBytes(iteration));
            hasher.AppendData(bytes);
            var computedHash = hasher.GetHashAndReset();

            fs.Write(computedHash);
        }

        File.Move(tempCheckpointFile, checkpointFile, overwrite: true);
    }
}
