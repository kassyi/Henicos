using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

namespace Henicos.Benchmarks.Repositories;

public interface ICheckpointRepository
{
    bool Exists();
    FermatPrpCheckpoint Load();
    void Save(FermatPrpCheckpoint checkpoint);
}
