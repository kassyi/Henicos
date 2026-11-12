#nullable enable
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// The kernels of <c>Native/module_arguments.cu</c> imported with <c>[CudaImport]</c>, beside the YAML binding
/// <c>Native/module_arguments.yml</c> of the same kernels (<see cref="ModuleArgumentTests"/>).
/// </summary>
public static partial class ModuleArgumentImports
{
    private const string Module = "module_arguments.gfatbin";

    [CudaImport(Module, EntryPoint = "RecordAddress")]
    public static partial void RecordAddress([In] float[] data, [Out] ulong[] seen);

    [CudaImport(Module, EntryPoint = "RecordAddress")]
    public static partial void RecordNullableAddress([In] float[]? data, [Out] ulong[] seen);

    [CudaImport(Module)]
    public static partial void UpdateByReference(ref int total, in int seed, out int result);

    [CudaImport(Module, EntryPoint = "UpdateByReference")]
    public static partial void UpdateByReadOnlyReference(ref int total, ref readonly int seed, out int result);

    [CudaImport(Module)]
    public static partial void UpdateByReferenceBounded(ref int total, in int seed, out int result, uint blockDimX);

    [CudaImport(Module)]
    public static partial void ShiftMatrix(float[,] x, int rows, int cols, uint blockDimX);
}
