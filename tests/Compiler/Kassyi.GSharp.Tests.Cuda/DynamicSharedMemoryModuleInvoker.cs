using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// Launches the hand-written <c>Native/dynamic_shared_memory_module_kernel.cu</c> as an external module (#820).
/// </summary>
public static partial class DynamicSharedMemoryModuleInvoker
{
    public const string FatbinName = "dynamic_shared_memory_module_kernel.gfatbin";
    public const string EntryPoint = "DynamicSharedMemoryModuleKernel";

    [CudaImport(FatbinName, EntryPoint = EntryPoint)]
    public static partial void LaunchFillAndSum([Out] uint[] output, int n, uint blockDimX, uint sharedMemBytes);
}
