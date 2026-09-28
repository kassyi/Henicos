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

    IntPtr GetNativeFunction() => IntPtr.Zero;
    IntPtr GetGSharpFunction()
    {
        if (string.IsNullOrEmpty(GSharpKernelName))
        {
            return IntPtr.Zero;
        }

        var hModule = Kassyi.GSharp.Generated.GSharpModuleContext_Henicos_Benchmarks.GetOrLoadModule();
        return GpuKernelLoader.GetFunction(hModule, GSharpKernelName);
    }
    string NativeKernelName => "";
    string GSharpKernelName => "";
}
