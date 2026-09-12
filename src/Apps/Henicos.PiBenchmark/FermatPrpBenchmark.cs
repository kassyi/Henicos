using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.Math.BigInt;

namespace Henicos.PiBenchmark;

public class FermatPrpBenchmark
{
    private const string CheckpointFile = "fermat_checkpoint.dat";
    private const string TempCheckpointFile = "fermat_checkpoint.tmp";

    public static void RunFermatTest(int E, int iterations)
    {
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info(" G# Native Fermat PRP Test (Stdlib FermatRunner)");
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info($"E       = {E}");
        Logger<FermatPrpBenchmark>.Info($"Digits  = {(2 * E * Math.Log10(2)):N0}");
        Logger<FermatPrpBenchmark>.Info("==================================================");

        using var scope = new GpuKernelLoader.CudaContextScope();

        var runner = new FermatRunner(E);

        var startIter = 0;

        if (File.Exists(CheckpointFile))
        {
            try
            {
                using var fs = File.OpenRead(CheckpointFile);
                using var br = new BinaryReader(fs);
                startIter = br.ReadInt32();

                var state = new uint[runner.OutNumWords];
                var bytes = MemoryMarshal.AsBytes(state.AsSpan());

                if (fs.Read(bytes) != bytes.Length)
                {
                    throw new Exception("Checkpoint file is corrupted or truncated.");
                }

                var storedHash = br.ReadBytes(32);
                if (storedHash.Length != 32)
                {
                    throw new Exception("Checkpoint file is missing SHA256 hash or truncated.");
                }

                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                hasher.AppendData(BitConverter.GetBytes(startIter));
                hasher.AppendData(bytes);
                var computedHash = hasher.GetHashAndReset();

                if (!computedHash.SequenceEqual(storedHash))
                {
                    throw new Exception("Checkpoint file hash mismatch! Data is corrupted.");
                }

                runner.LoadState(state);
                Logger<FermatPrpBenchmark>.Info($"Resuming from iteration {startIter} (Hash OK)...");
            }
            catch (Exception ex)
            {
                Logger<FermatPrpBenchmark>.Warning($"Warning: Failed to load checkpoint ({ex.Message}). Starting from scratch.");
                startIter = 0;
            }
        }
        else
        {
            Logger<FermatPrpBenchmark>.Info($"Starting {iterations} iterations of Fermat PRP Test from scratch...");
        }

        var sw = Stopwatch.StartNew();

        var saveInterval = 100000;
        var tdrChunkSize = 100;

        for (var i = startIter; i < iterations;)
        {
            var nextSave = Math.Min(i + saveInterval, iterations);

            while (i < nextSave)
            {
                var chunk = Math.Min(tdrChunkSize, nextSave - i);
                runner.RunBenchmarkChunk(chunk);
                i += chunk;
            }

            // Save state to disk
            SaveCheckpoint(i, runner);
            Logger<FermatPrpBenchmark>.Info($"[Checkpoint] Saved state and hash at iteration {i}.");
        }

        sw.Stop();
        Logger<FermatPrpBenchmark>.Info($"Completed all requested iterations up to {iterations} in {sw.ElapsedMilliseconds} ms");
    }

    private static void SaveCheckpoint(int currentIteration, FermatRunner runner)
    {
        using (var fs = File.Create(TempCheckpointFile))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(currentIteration);
            var state = runner.GetState();
            var bytes = MemoryMarshal.AsBytes(state.AsSpan());
            fs.Write(bytes);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hasher.AppendData(BitConverter.GetBytes(currentIteration));
            hasher.AppendData(bytes);
            var computedHash = hasher.GetHashAndReset();

            fs.Write(computedHash);
        }

        File.Move(TempCheckpointFile, CheckpointFile, overwrite: true);
    }
}
