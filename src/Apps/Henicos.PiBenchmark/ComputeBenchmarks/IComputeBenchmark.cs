namespace Henicos.PiBenchmark.ComputeBenchmarks;

public interface IComputeBenchmark
{
    string Name { get; }
    void Initialize();
    void RunReferenceCpu();
    void RunGSharpCpu();
    void Verify();
}
