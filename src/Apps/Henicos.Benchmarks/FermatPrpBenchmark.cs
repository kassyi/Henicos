using Henicos.Benchmarks.Repositories;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Henicos.Benchmarks;

public class FermatPrpBenchmark
{
    private const string CheckpointFile = "fermat_checkpoint.dat";
    private const string TempCheckpointFile = "fermat_checkpoint.tmp";

    /// <summary>
    /// Runs the Fermat PRP test of <c>N = 2^(2E+1) - 2^E + 11</c> to base <paramref name="base"/>, saving every verified
    /// checkpoint to disk and resuming from the saved one when it exists.
    /// </summary>
    public static void RunFermatTest(int E, uint @base)
    {
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info(" G# Native Fermat PRP Test (Stdlib FermatAlgorithm)");
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info($"E       = {E}");
        Logger<FermatPrpBenchmark>.Info($"Base    = {@base}");
        Logger<FermatPrpBenchmark>.Info($"Digits  = {(2 * E * Math.Log10(2)):N0}");
        Logger<FermatPrpBenchmark>.Info("==================================================");

        using var scope = new GpuKernelLoader.CudaContextScope();

        var repository = new FileCheckpointRepository(CheckpointFile, TempCheckpointFile);
        var options = new FermatPrpOptions
        {
            OnVerifiedCheckpoint = checkpoint =>
            {
                repository.Save(checkpoint);
                Logger<FermatPrpBenchmark>.Info(
                    $"[Checkpoint] Saved verified iteration {checkpoint.Iteration} (checks {checkpoint.ChecksPassed}, rollbacks {checkpoint.Rollbacks}).");
            }
        };

        using var runner = new FermatAlgorithm(E, @base, options);
        if (repository.Exists())
        {
            // Throws for a file of another test or of the earlier format, which cannot be resumed.
            var checkpoint = repository.Load();
            runner.Restore(checkpoint);
            Logger<FermatPrpBenchmark>.Info($"Resuming from verified iteration {checkpoint.Iteration} (Hash OK)...");
        }
        else
        {
            Logger<FermatPrpBenchmark>.Info($"Starting the {runner.ChainLength} squarings of the test from scratch...");
        }

        var result = runner.Run();

        Logger<FermatPrpBenchmark>.Info($"Probable prime : {result.IsProbablePrime} (small divisor: {result.SmallDivisor?.ToString() ?? "none"})");
        Logger<FermatPrpBenchmark>.Info($"Residue64      : {result.Residue64:X16} (check {result.CheckResidue64:X16})");
        Logger<FermatPrpBenchmark>.Info($"Checks passed  : {result.ChecksPassed}, rollbacks {result.Rollbacks}");
        Logger<FermatPrpBenchmark>.Info($"Completed {result.Iterations} squarings in {result.Elapsed.TotalMilliseconds:F0} ms");
    }
}
