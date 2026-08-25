using System.Reflection;
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

        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly,
            (libraryName, assembly, _) => DlImportResolover(libraryName, assembly));
    }

    private static IntPtr DlImportResolover(string libraryName, Assembly assembly)
    {
        if (libraryName != DllName && libraryName != DllName + ".dll")
        {
            return IntPtr.Zero;
        }

        // Fallbacks for test runners with shadow copying
        string[] possiblePaths =
        [
            Path.Combine(AppContext.BaseDirectory, DllName + ".dll"),
            Path.Combine(Path.GetDirectoryName(assembly.Location) ?? string.Empty, DllName + ".dll"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "Native",
                "Kassyi.GSharp.Stdlib.Math.Cuda", DllName + ".dll") // Fallback to source
        ];

        foreach (var path in possiblePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(Path.GetFullPath(path), out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string lpPathName);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_Init))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr GpuBigInt_Init(int maxWords);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_CopyDeviceToDevice))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_CopyDeviceToDevice(IntPtr deviceDestination, IntPtr deviceSource, int numWords,
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
    public static partial void GpuBigInt_Free(IntPtr devicePointer);

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

