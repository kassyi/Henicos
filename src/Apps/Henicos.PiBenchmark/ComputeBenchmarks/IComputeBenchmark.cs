namespace Henicos.PiBenchmark.ComputeBenchmarks;

public interface IComputeBenchmark
{
    string Name { get; }
    void Initialize();
    void RunReferenceCpu();
    void RunGSharp();
    void RunGSharpCpu() => RunGSharp();
    void RunNativeCuda();
    void Verify();
}
