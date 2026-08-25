using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace Kassyi.GSharp.Stdlib.Math.Interop;

internal static partial class NativeMethods
{
    private const string DllName = "Kassyi.GSharp.GpuBigInt.Cuda";

    static NativeMethods()
    {
        var binPath = CudaEnvironment.GetCudaBinPath();
        if (string.IsNullOrEmpty(binPath))
        {
            return;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null && !pathEnv.Contains(binPath))
        {
            Environment.SetEnvironmentVariable("PATH", binPath + Path.PathSeparator + pathEnv);
        }

        _ = SetDllDirectory(binPath);
    }

    [LibraryImport("kernel32.dll", EntryPoint = nameof(SetDllDirectory), SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string lpPathName);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_Init))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr GpuBigInt_Init(int maxWords);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_CopyDeviceToDevice))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_CopyDeviceToDevice(IntPtr dDst, IntPtr dSrc, int numWords,
        IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_Square))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_Square(IntPtr workspace, IntPtr data, int numWords, IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_Add))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_Add(IntPtr workspace, IntPtr deviceA, IntPtr deviceB, IntPtr deviceResult,
        int numWords,
        IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_Sub))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_Sub(IntPtr workspace, IntPtr deviceA, IntPtr deviceB, IntPtr deviceResult,
        int numWords,
        IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_ShiftLeft))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_ShiftLeft(IntPtr workspace, IntPtr deviceA, int shiftBits, IntPtr deviceResult,
        int numWords, IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_ShiftRight))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_ShiftRight(IntPtr workspace, IntPtr deviceA, int shiftBits,
        IntPtr deviceResult,
        int numWords, IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_BitwiseAnd))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_BitwiseAnd(IntPtr workspace, IntPtr deviceA, IntPtr deviceB,
        IntPtr deviceResult,
        int numWords, IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_Alloc))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr GpuBigInt_Alloc(int numWords);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_Free))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void GpuBigInt_Free(IntPtr ptr);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_Cleanup))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void GpuBigInt_Cleanup(IntPtr workspace);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_CopyHostToDevice))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_CopyHostToDevice(IntPtr deviceDestination, uint[] hostSource, int numWords,
        IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_CopyDeviceToHost))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_CopyDeviceToHost(uint[] hostDestination, IntPtr deviceSource, int numWords,
        IntPtr stream = 0);
}

