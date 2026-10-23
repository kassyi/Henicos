namespace Henicos.Benchmarks.ComputeBenchmarks;

/// <summary>
/// Measures the launch overhead of a kernel that does nothing. There is no output to compare, so verification only
/// confirms that both launches completed.
/// </summary>
public sealed class EmptyKernelBenchmark : IComputeBenchmark
{
    private bool _gsharpLaunched;
    private bool _nativeLaunched;

    public string Name => "Empty Kernel (launch overhead)";

    public void Initialize()
    {
    }

    public void RunReferenceCpu()
    {
        // Nothing to compute on the host
    }

    public void RunGSharp()
    {
        EmptyKernelKernels.LaunchEmptyKernel(gridDimX: 1, blockDimX: 1);
        _gsharpLaunched = true;
    }

    public void RunNativeCuda()
    {
        NativeKernelInvoker.LaunchEmpty(gridDimX: 1, blockDimX: 1);
        _nativeLaunched = true;
    }

    public void Verify()
    {
        if (!_gsharpLaunched || !_nativeLaunched)
        {
            throw new InvalidOperationException("The empty kernel was not launched by both back ends");
        }
    }

    public IntPtr GetNativeFunction() => NativeKernelInvoker.FunctionEmpty;
    public string NativeKernelName => "EmptyKernel_Native";
    public string GSharpKernelName => "__gsharp_k_Henicos__Benchmarks__ComputeBenchmarks__EmptyKernelKernels__EmptyKernel";
}
