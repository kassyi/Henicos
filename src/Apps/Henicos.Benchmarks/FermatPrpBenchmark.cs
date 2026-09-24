namespace Henicos.Benchmarks;

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
        var repository = new Repositories.FileCheckpointRepository(CheckpointFile, TempCheckpointFile);

        var startIter = 0;

        if (repository.Exists())
        {
            var (loadedIter, state) = repository.Load(runner.OutNumWords);
            startIter = loadedIter;
            runner.LoadState(state);
            Logger<FermatPrpBenchmark>.Info($"Resuming from iteration {startIter} (Hash OK)...");
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
            repository.Save(i, runner.GetState());
            Logger<FermatPrpBenchmark>.Info($"[Checkpoint] Saved state and hash at iteration {i}.");
        }

        sw.Stop();
        Logger<FermatPrpBenchmark>.Info($"Completed all requested iterations up to {iterations} in {sw.ElapsedMilliseconds} ms");
    }
}
