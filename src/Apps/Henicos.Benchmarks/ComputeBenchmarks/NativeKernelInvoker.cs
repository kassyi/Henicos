
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
    public static IntPtr FunctionEmpty => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.EmptyKernel_Native);
    public static IntPtr FunctionMemoryCopy => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.MemoryCopyKernel_Native);
    public static IntPtr FunctionMathFma => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.MathFmaKernel_Native);
    public static IntPtr FunctionBranchDivergence => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.BranchDivergenceKernel_Native);
    public static IntPtr FunctionMandelbrot => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.MandelbrotKernel_Native);
    public static IntPtr FunctionTranspose => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, NativeCudaKernels.TransposeKernel_Native);

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

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.EmptyKernel_Native)]
    public static partial void LaunchEmpty(uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.MemoryCopyKernel_Native)]
    public static partial void LaunchMemoryCopy(
        [In] float[] src, [Out] float[] dst, int n,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.MathFmaKernel_Native)]
    public static partial void LaunchMathFma(
        float[] data, int n, int iterations,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.BranchDivergenceKernel_Native)]
    public static partial void LaunchBranchDivergence(
        float[] data, int n,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.MandelbrotKernel_Native)]
    public static partial void LaunchMandelbrot(
        [Out] int[] output, int width, int height, int maxIter, float xMin, float xMax, float yMin, float yMax,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY);

    [CudaImport(FatbinName, EntryPoint = NativeCudaKernels.TransposeKernel_Native)]
    public static partial void LaunchTranspose(
        [In] float[] input, [Out] float[] output, int width, int height,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY);
}
