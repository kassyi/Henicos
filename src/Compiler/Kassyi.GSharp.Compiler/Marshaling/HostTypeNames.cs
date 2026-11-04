namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Fully qualified type and member names of the runtime and BCL emitted into host launch wrappers.
/// </summary>
internal static class HostTypeNames
{
    public const string CudaDriverApi = "global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi";
    public const string CudaException = "global::Kassyi.GSharp.Runtime.Interop.CudaException";
    public const string IntPtr = "global::System.IntPtr";
    public const string Unsafe = "global::System.Runtime.CompilerServices.Unsafe";
    public const string GCHandle = "global::System.Runtime.InteropServices.GCHandle";
    public const string GCHandleType = "global::System.Runtime.InteropServices.GCHandleType";
    public const string ArrayPoolShared = "global::System.Buffers.ArrayPool<byte>.Shared";
}
