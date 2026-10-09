using System.ComponentModel;
using System.Reflection;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace Kassyi.GSharp.Stdlib.GpuMath;

/// <summary>P/Invoke bindings to the legacy pre-compiled native CUDA C++ dynamic library (<c>Kassyi.GSharp.GpuBigInt.Cuda.dll</c>).</summary>
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
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

        if (!SetDllDirectory(binPath))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to SetDllDirectory for CUDA bin path.");
        }

        NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly,
            (libraryName, assembly, _) => DlImportResolover(libraryName, assembly));
    }

    private static IntPtr DlImportResolover(string libraryName, Assembly assembly)
    {
        if (libraryName != DllName && libraryName != DllName + ".dll")
        {
            return IntPtr.Zero;
        }

        string[] possiblePaths =
        [
            Path.Combine(AppContext.BaseDirectory, DllName + ".dll"),
            Path.Combine(Path.GetDirectoryName(assembly.Location) ?? string.Empty, DllName + ".dll")
        ];

        var triedPaths = new List<string>();
        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            triedPaths.Add(fullPath);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            if (NativeLibrary.TryLoad(fullPath, out var handle))
            {
                return handle;
            }
        }

        throw new DllNotFoundException($"Failed to load native library '{libraryName}'. Tried paths:\n{string.Join("\n", triedPaths)}");
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

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigInt_ClearMemory))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigInt_ClearMemory(IntPtr devicePointer, int numWords, IntPtr stream = 0);

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

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigFloat_DivideByUInt32))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigFloat_DivideByUInt32(IntPtr workspace, IntPtr deviceA, IntPtr deviceResult, int numWords, uint divisor, out uint remainder, IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigFloat_Normalize))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigFloat_Normalize(IntPtr workspace, IntPtr deviceA, int numWordsA, IntPtr deviceResult, int numWordsResult, int valueExp, out int resultExp, out int isZero, IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigFloat_AddSub))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigFloat_AddSub(
        IntPtr workspace,
        IntPtr leftSig, int leftNumWords, int leftExp, int leftSign,
        IntPtr rightSig, int rightNumWords, int rightExp, int rightSign,
        int isSubtract,
        IntPtr shiftedBuffer,
        IntPtr tempResult,
        IntPtr resultSig,
        int numWordsResult,
        out int resultExp, out int resultSign, out int isZero,
        IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigFloat_Reciprocal))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigFloat_Reciprocal(IntPtr workspace, IntPtr deviceA, IntPtr deviceResult, int numWords, int marginWords, IntPtr stream = 0);

    [LibraryImport(DllName, EntryPoint = nameof(GpuBigFloat_Multiply))]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool GpuBigFloat_Multiply(
        IntPtr workspace,
        IntPtr deviceA, int numWordsA,
        IntPtr deviceB, int numWordsB,
        IntPtr deviceResult, int numWordsResult,
        int tempExp, int tempSign,
        out int resultExp, out int resultSign,
        IntPtr stream = 0);
}


