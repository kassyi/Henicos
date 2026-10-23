using Kassyi.GSharp.Generated;
namespace Henicos.Benchmarks.ComputeBenchmarks;

public interface IComputeBenchmark
{
    string Name
    {
        get;
    }
    void Initialize();
    void RunReferenceCpu();
    void RunGSharp();
    void RunGSharpCpu() => RunGSharp();
    void RunNativeCuda();
    void Verify();

    /// <summary>Floating-point operations one run performs; 0 when the benchmark is not compute-bound.</summary>
    double TheoreticalFlops => 0;

    /// <summary>Bytes one run reads or writes in global memory; 0 when the benchmark is not memory-bound.</summary>
    double TheoreticalBytes => 0;

    IntPtr GetNativeFunction() => IntPtr.Zero;
    IntPtr GetGSharpFunction()
    {
        if (string.IsNullOrEmpty(GSharpKernelName))
        {
            return IntPtr.Zero;
        }

        var hModule = GSharpModuleContext_Henicos_Benchmarks.GetOrLoadModule();
        return GpuKernelLoader.GetFunction(hModule, GSharpKernelName);
    }
    string NativeKernelName => "";
    string GSharpKernelName => "";
}
