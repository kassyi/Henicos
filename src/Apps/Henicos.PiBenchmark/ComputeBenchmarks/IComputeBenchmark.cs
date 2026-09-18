namespace Henicos.PiBenchmark.ComputeBenchmarks;

public interface IComputeBenchmark
{
    string Name { get; }
    void Initialize();
    void RunReferenceCpu();

    // E2E Measurements (Allocation + HtoD + Kernel + DtoH + Free)
    void RunGSharp();
    void RunGSharpCpu() => RunGSharp();

    void RunNativeCuda();

    // Pure Kernel Measurements (Resident Device Buffers)
    void InitializeDeviceMemory();
    void RunGSharp_Pure();
    void RunNativeCuda_Pure();
    void CleanupDeviceMemory();

    void Verify();
}
