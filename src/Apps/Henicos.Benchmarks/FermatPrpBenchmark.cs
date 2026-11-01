using System.Numerics;
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
    public static void RunFermatTest(int E, uint @base, string? proofPath = null, int proofPower = 0)
    {
        var resolvedProofPower = proofPath != null
            ? (proofPower > 0 ? proofPower : Math.Clamp(BitOperations.Log2((uint)Math.Max(2, E)), 1, 8))
            : 0;
        var leafFilePath = proofPath != null ? $"{proofPath}.leaves" : null;

        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info(" G# Native Fermat PRP Test (Stdlib FermatAlgorithm)");
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info($"E       = {E}");
        Logger<FermatPrpBenchmark>.Info($"Base    = {@base}");
        Logger<FermatPrpBenchmark>.Info($"Digits  = {(2 * E * Math.Log10(2)):N0}");
        if (proofPath != null)
        {
            Logger<FermatPrpBenchmark>.Info($"Proof   = {proofPath} (k = {resolvedProofPower}, leaves = {1 << resolvedProofPower})");
        }

        Logger<FermatPrpBenchmark>.Info("==================================================");

        using var scope = new GpuKernelLoader.CudaContextScope();

        var repository = new FileCheckpointRepository(CheckpointFile, TempCheckpointFile);
        var options = new FermatPrpOptions
        {
            ProofPower = resolvedProofPower,
            ProofLeafFile = leafFilePath,
            OnVerifiedCheckpoint = checkpoint =>
            {
                repository.Save(checkpoint);
                Logger<FermatPrpBenchmark>.Info(
                    $"[Checkpoint] Saved verified iteration {checkpoint.Iteration} (checks {checkpoint.ChecksPassed}, rollbacks {checkpoint.Rollbacks}).");
            }
        };

        FermatPrpResult result;
        FermatPrpProof? finalizedProof;
        using (var runner = new FermatAlgorithm(E, @base, options))
        {
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

            result = runner.Run();
            finalizedProof = runner.LastProof;
        }

        if (proofPath != null && finalizedProof != null)
        {
            finalizedProof.Save(proofPath);
            Logger<FermatPrpBenchmark>.Info(
                $"Saved Pietrzak proof to {proofPath} (ProofPower = {finalizedProof.ProofPower}, Witnesses = {finalizedProof.Witnesses.Count}).");
            if (leafFilePath != null && File.Exists(leafFilePath))
            {
                File.Delete(leafFilePath);
            }
        }

        Logger<FermatPrpBenchmark>.Info($"Probable prime : {result.IsProbablePrime} (small divisor: {result.SmallDivisor?.ToString() ?? "none"})");
        Logger<FermatPrpBenchmark>.Info($"Residue64      : {result.Residue64:X16} (check {result.CheckResidue64:X16})");
        Logger<FermatPrpBenchmark>.Info($"Checks passed  : {result.ChecksPassed}, rollbacks {result.Rollbacks}");
        Logger<FermatPrpBenchmark>.Info($"Completed {result.Iterations} squarings in {result.Elapsed.TotalMilliseconds:F0} ms");
    }

    /// <summary>
    /// Loads a binary Pietrzak proof from <paramref name="proofPath"/> and verifies it on the GPU (or host for small <c>E</c>).
    /// </summary>
    public static bool VerifyProofFile(string proofPath)
    {
        var proof = FermatPrpProof.Load(proofPath);
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info(" G# Pietrzak PRP Proof Verifier");
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info($"Proof file   = {proofPath}");
        Logger<FermatPrpBenchmark>.Info($"E            = {proof.E:N0}");
        Logger<FermatPrpBenchmark>.Info($"Base         = {proof.Base}");
        Logger<FermatPrpBenchmark>.Info($"ProofPower   = {proof.ProofPower} (Witnesses = {proof.Witnesses.Count})");
        Logger<FermatPrpBenchmark>.Info($"ResidueWords = {proof.ResidueWords:N0}");
        Logger<FermatPrpBenchmark>.Info("==================================================");

        using var scope = new GpuKernelLoader.CudaContextScope();
        var verification = FermatAlgorithm.VerifyProof(proof);

        Logger<FermatPrpBenchmark>.Info($"Proof valid    : {verification.IsValid}");
        if (!verification.IsValid && verification.FailureReason != null)
        {
            Logger<FermatPrpBenchmark>.Warning($"Failure reason : {verification.FailureReason}");
        }

        Logger<FermatPrpBenchmark>.Info($"Probable prime : {verification.IsProbablePrime}");
        Logger<FermatPrpBenchmark>.Info($"Residue64      : {verification.Residue64:X16} (check {verification.CheckResidue64:X16})");
        Logger<FermatPrpBenchmark>.Info($"Squarings      : {verification.Squarings:N0} in {verification.Elapsed.TotalMilliseconds:F1} ms");
        Logger<FermatPrpBenchmark>.Info("==================================================");
        return verification.IsValid;
    }

    /// <summary>
    /// Measures steady-state GPU squaring-and-fold throughput at exponent <paramref name="E"/> after warming up kernels
    /// and capturing the 256-step CUDA Graph.
    /// </summary>
    public static void RunThroughputBenchmark(int E, uint @base, int iterations = 512)
    {
        var roundedIterations = Math.Max(256, ((iterations + 255) / 256) * 256);
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info(" G# Fermat PRP Steady-State Throughput Benchmark");
        Logger<FermatPrpBenchmark>.Info("==================================================");
        Logger<FermatPrpBenchmark>.Info($"E                = {E:N0}");
        Logger<FermatPrpBenchmark>.Info($"Base             = {@base}");
        Logger<FermatPrpBenchmark>.Info($"Modulus bits     = {2L * E + 1:N0} (~{(2.0 * E * Math.Log10(2)):N0} digits)");
        Logger<FermatPrpBenchmark>.Info($"Measure steps    = {roundedIterations:N0} (256-step CUDA Graph replay)");
        Logger<FermatPrpBenchmark>.Info("==================================================");

        using var scope = new GpuKernelLoader.CudaContextScope();
        var initSw = Stopwatch.StartNew();
        using var runner = new FermatAlgorithm(E, @base);
        initSw.Stop();
        Logger<FermatPrpBenchmark>.Info(
            $"Initialized chain buffers & twiddles in {initSw.Elapsed.TotalMilliseconds:F1} ms (ResidueWords = {runner.ResidueWords:N0})");

        // 1 eager step loads CUDA modules; 256 steps capture and replay the full 256-squaring CUDA Graph once.
        var warmupSw = Stopwatch.StartNew();
        runner.RunBenchmarkChunk(1);
        runner.RunBenchmarkChunk(256);
        warmupSw.Stop();
        Logger<FermatPrpBenchmark>.Info($"Warm-up + 256-step CUDA Graph capture completed in {warmupSw.Elapsed.TotalMilliseconds:F1} ms");

        var measureSw = Stopwatch.StartNew();
        runner.RunBenchmarkChunk(roundedIterations);
        measureSw.Stop();

        var totalMs = measureSw.Elapsed.TotalMilliseconds;
        var msPerStep = totalMs / roundedIterations;
        var stepsPerSec = 1000.0 / msPerStep;
        var projectedDays = (runner.ChainLength * msPerStep) / (1000.0 * 86400.0);

        Logger<FermatPrpBenchmark>.Info("--------------------------------------------------");
        Logger<FermatPrpBenchmark>.Info($"Elapsed ({roundedIterations} steps) : {totalMs:F2} ms");
        Logger<FermatPrpBenchmark>.Info($"Throughput            : {msPerStep:F3} ms/step ({stepsPerSec:F1} steps/s)");
        Logger<FermatPrpBenchmark>.Info($"Projected full run    : {projectedDays:F2} days ({runner.ChainLength:N0} squarings)");
        Logger<FermatPrpBenchmark>.Info("==================================================");
    }
}
