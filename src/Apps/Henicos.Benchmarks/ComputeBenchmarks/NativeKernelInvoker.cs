
namespace Henicos.Benchmarks.ComputeBenchmarks;

/// <summary>
/// Launches the hand-written CUDA kernels of <c>Native/*.cu</c> (compiled into <see cref="FatbinName"/>) that read or
/// write an array in one direction only, and loads every native kernel for the resource columns. The kernels whose
/// arrays are copied both ways are bound in <c>Native/native_benchmarks.yml</c> (<see cref="NativeBenchmarkKernels"/>).
/// </summary>
public static partial class NativeKernelInvoker
{
    public const string FatbinName = "native_benchmarks.gfatbin";

    private const string Sgemm = "SgemmKernel_Native";
    private const string Sha256 = "Sha256Kernel_Native";
    private const string BlackScholes = "BlackScholesKernel_Native";
    private const string Reduction = "ReductionKernel_Native";
    private const string MemoryCopy = "MemoryCopyKernel_Native";
    private const string Mandelbrot = "MandelbrotKernel_Native";
    private const string Transpose = "TransposeKernel_Native";

    private static readonly Lazy<byte[]> s_fatbin = new(() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, FatbinName)));

    public static IntPtr FunctionSgemm => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, Sgemm);
    public static IntPtr FunctionSha256 => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, Sha256);
    public static IntPtr FunctionBlackScholes => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, BlackScholes);
    public static IntPtr FunctionReduction => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, Reduction);
    public static IntPtr FunctionEmpty => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, "EmptyKernel_Native");
    public static IntPtr FunctionMemoryCopy => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, MemoryCopy);
    public static IntPtr FunctionMathFma => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, "MathFmaKernel_Native");
    public static IntPtr FunctionBranchDivergence =>
        GpuKernelLoader.LoadAotKernel(s_fatbin.Value, "BranchDivergenceKernel_Native");
    public static IntPtr FunctionMandelbrot => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, Mandelbrot);
    public static IntPtr FunctionTranspose => GpuKernelLoader.LoadAotKernel(s_fatbin.Value, Transpose);

    [CudaImport(FatbinName, EntryPoint = Sgemm)]
    public static partial void LaunchSgemm(
        [In] float[] a, [In] float[] b, [Out] float[] c, int n,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY);

    [CudaImport(FatbinName, EntryPoint = Sha256)]
    public static partial void LaunchSha256(
        [In] byte[] input, [Out] uint[] output, int numMessages,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = BlackScholes, PackContext = true)]
    public static partial void LaunchBlackScholes(
        [In] float[] s, [In] float[] x, [In] float[] t, float r, float v,
        [Out] float[] callResult, [Out] float[] putResult, int n,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = Reduction)]
    public static partial void LaunchReduction(
        [In] Float4[] input, [Out] float[] output, int n,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = MemoryCopy)]
    public static partial void LaunchMemoryCopy(
        [In] float[] src, [Out] float[] dst, int n,
        uint gridDimX, uint blockDimX);

    [CudaImport(FatbinName, EntryPoint = Mandelbrot)]
    public static partial void LaunchMandelbrot(
        [Out] int[] output, int width, int height, int maxIter, float xMin, float xMax, float yMin, float yMax,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY);

    [CudaImport(FatbinName, EntryPoint = Transpose)]
    public static partial void LaunchTranspose(
        [In] float[] input, [Out] float[] output, int width, int height,
        uint gridDimX, uint gridDimY, uint blockDimX, uint blockDimY);
}
