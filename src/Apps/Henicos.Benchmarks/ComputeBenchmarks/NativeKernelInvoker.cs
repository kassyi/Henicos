
namespace Henicos.Benchmarks.ComputeBenchmarks;

public static partial class NativeKernelInvoker
{
    public const string FatbinName = "native_benchmarks.gfatbin";

    private static readonly Lazy<byte[]> s_fatbin = new(() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, FatbinName)));

    public static IntPtr FunctionSgemm => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.SgemmKernel_Native);
    public static IntPtr FunctionSha256 => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.Sha256Kernel_Native);
    public static IntPtr FunctionBlackScholes => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.BlackScholesKernel_Native);
    public static IntPtr FunctionReduction => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.ReductionKernel_Native);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.SgemmKernel_Native)]
    public static partial void LaunchSgemm(
        [In] float[] a, [In] float[] b, [Out] float[] c, int n,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.Sha256Kernel_Native)]
    public static partial void LaunchSha256(
        [In] byte[] input, [Out] uint[] output, int numMessages,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.BlackScholesKernel_Native, PackContext = true)]
    public static partial void LaunchBlackScholes(
        [In] float[] s, [In] float[] x, [In] float[] t, float r, float v,
        [Out] float[] callResult, [Out] float[] putResult, int n,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.ReductionKernel_Native)]
    public static partial void LaunchReduction(
        [In] Float4[] input, [Out] float[] output, int n,
        uint gridDimX, uint blockDimX);
}
