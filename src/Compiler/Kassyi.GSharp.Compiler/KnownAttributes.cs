namespace Kassyi.GSharp.Compiler;

/// <summary>Defines well-known attribute names and reflection-free inspection helpers used during source generation.</summary>
/// <seealso cref="TypeConstants"/>
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public constants for compiler internals")]
public static class KnownAttributes
{
    public const string GpuKernel = "GpuKernel";
    public const string GpuKernelAttribute = "GpuKernelAttribute";

    public const string GpuDeviceFunction = "GpuDeviceFunction";
    public const string GpuDeviceFunctionAttribute = "GpuDeviceFunctionAttribute";

    public const string GpuConst = "GpuConst";
    public const string GpuConstAttribute = "GpuConstAttribute";

    public const string GpuInline = "GpuInline";
    public const string GpuInlineAttribute = "GpuInlineAttribute";

    public const string GpuNoInline = "GpuNoInline";
    public const string GpuNoInlineAttribute = "GpuNoInlineAttribute";

    public const string JitConstant = "JitConstant";
    public const string JitConstantAttribute = "JitConstantAttribute";

    public const string CudaIntrinsic = "CudaIntrinsic";
    public const string CudaIntrinsicAttribute = "CudaIntrinsicAttribute";

    public const string CudaIntrinsicPattern = "CudaIntrinsicPattern";
    public const string CudaIntrinsicPatternAttribute = "CudaIntrinsicPatternAttribute";

    // Check helper
    public static bool IsGpuKernel(string? name) => name is GpuKernel or GpuKernelAttribute;

    public static bool IsGpuDeviceFunction(string? name) =>
        name is GpuDeviceFunction or GpuDeviceFunctionAttribute;

    public static bool IsGpuConst(string? name) => name is GpuConst or GpuConstAttribute;

    public static bool IsJitConstant(string? name) => name is JitConstant or JitConstantAttribute;
}
