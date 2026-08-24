using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]

namespace Kassyi.GSharp.Stdlib.Math.Interop;

internal static partial class NativeMethods
{
    private const string DllName = "Kassyi.GSharp.GpuBigInt.Cuda";

    static NativeMethods()
    {
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryA", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string lpPathName);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_Init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr GpuBigInt_Init(int maxWords);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_CopyDeviceToDevice")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_CopyDeviceToDevice(IntPtr d_dst, IntPtr d_src, int numWords, IntPtr stream = default);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_Square")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_Square(IntPtr workspace, IntPtr data, int numWords, IntPtr stream = default);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_Add")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_Add(IntPtr workspace, IntPtr d_a, IntPtr d_b, IntPtr d_result, int numWords, IntPtr stream = default);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_Sub")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_Sub(IntPtr workspace, IntPtr d_a, IntPtr d_b, IntPtr d_result, int numWords, IntPtr stream = default);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_ShiftLeft")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_ShiftLeft(IntPtr workspace, IntPtr d_a, int shiftBits, IntPtr d_result, int numWords, IntPtr stream = default);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_ShiftRight")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_ShiftRight(IntPtr workspace, IntPtr d_a, int shiftBits, IntPtr d_result, int numWords, IntPtr stream = default);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_BitwiseAnd")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_BitwiseAnd(IntPtr workspace, IntPtr d_a, IntPtr d_b, IntPtr d_result, int numWords, IntPtr stream = default);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_Alloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr GpuBigInt_Alloc(int numWords);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_Free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void GpuBigInt_Free(IntPtr ptr);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_Cleanup")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void GpuBigInt_Cleanup(IntPtr workspace);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_CopyHostToDevice")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_CopyHostToDevice(IntPtr d_dst, uint[] h_src, int numWords, IntPtr stream = default);

    [LibraryImport(DllName, EntryPoint = "GpuBigInt_CopyDeviceToHost")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_CopyDeviceToHost(uint[] h_dst, IntPtr d_src, int numWords, IntPtr stream = default);
}

