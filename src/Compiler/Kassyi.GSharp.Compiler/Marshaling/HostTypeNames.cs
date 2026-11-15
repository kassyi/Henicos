namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Fully qualified type and member names of the runtime and BCL emitted into host launch wrappers.
/// </summary>
internal static class HostTypeNames
{
    public const string CudaDriverApi = "global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi";
    public const string CudaException = "global::Kassyi.GSharp.Runtime.Interop.CudaException";
    public const string GpuKernelLoader = "global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader";
    public const string CudaContextScope = GpuKernelLoader + ".CudaContextScope";
    public const string GpuBuffer = "global::Kassyi.GSharp.Runtime.GpuBuffer";
    public const string LaunchErrorRecords = "global::Kassyi.GSharp.Runtime.Aot.LaunchErrorRecords";
    public const string IntPtr = "global::System.IntPtr";
    public const string ArgumentNullException = "global::System.ArgumentNullException";
    public const string ArgumentException = "global::System.ArgumentException";
    public const string ArgumentOutOfRangeException = "global::System.ArgumentOutOfRangeException";
    public const string NotSupportedException = "global::System.NotSupportedException";
    public const string CpuKernelLoader = "global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader";
    public const string ArrayProxy = "global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy";
    public const string ArrayPinning = "global::Kassyi.GSharp.Runtime.Interop.ArrayPinning";
    public const string Unsafe = "global::System.Runtime.CompilerServices.Unsafe";
    public const string GCHandle = "global::System.Runtime.InteropServices.GCHandle";
    public const string GCHandleType = "global::System.Runtime.InteropServices.GCHandleType";
    public const string StructLayout = "global::System.Runtime.InteropServices.StructLayout";
    public const string LayoutKind = "global::System.Runtime.InteropServices.LayoutKind";
    public const string FieldOffset = "global::System.Runtime.InteropServices.FieldOffset";
    public const string PointerList = "global::System.Collections.Generic.List<global::System.IntPtr>";
    public const string ArrayPoolShared = "global::System.Buffers.ArrayPool<byte>.Shared";
    public const string Span = "global::System.Span";
    public const string ReadOnlySpan = "global::System.ReadOnlySpan";
    public const string ModuleKernelArguments = "global::Kassyi.GSharp.Runtime.Aot.ModuleKernelArguments";
    public const string Lazy = "global::System.Lazy";
    public const string File = "global::System.IO.File";
    public const string Path = "global::System.IO.Path";
    public const string AppContext = "global::System.AppContext";
}
