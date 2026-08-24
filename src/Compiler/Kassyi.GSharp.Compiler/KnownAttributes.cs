namespace Kassyi.GSharp.Compiler;

// ReSharper disable UnusedMember.Global
public static class KnownAttributes
{
    public const string GpuKernel = "GpuKernel";
    public const string GpuKernelAttribute = "GpuKernelAttribute";

    public const string GpuDevice = "GpuDevice";
    public const string GpuDeviceAttribute = "GpuDeviceAttribute";

    public const string GpuConst = "GpuConst";
    public const string GpuConstAttribute = "GpuConstAttribute";

    public const string GpuInline = "GpuInline";
    public const string GpuInlineAttribute = "GpuInlineAttribute";

    public const string GpuNoInline = "GpuNoInline";
    public const string GpuNoInlineAttribute = "GpuNoInlineAttribute";

    public const string JitConstant = "JitConstant";
    public const string JitConstantAttribute = "JitConstantAttribute";

    // Check helper
    public static bool IsGpuKernel(string? name) => name == GpuKernel || name == GpuKernelAttribute;
    public static bool IsGpuDevice(string? name) => name == GpuDevice || name == GpuDeviceAttribute;
    public static bool IsGpuConst(string? name) => name == GpuConst || name == GpuConstAttribute;
    public static bool IsJitConstant(string? name) => name == JitConstant || name == JitConstantAttribute;
}
