namespace Kassyi.GSharp.Compiler.Models;

/// <summary>The transpiled outputs of one <c>[GpuKernel]</c>, for both native dialects and the managed fallback.</summary>
/// <remarks>
/// A construct that a dialect could not emit turns that dialect's source into <c>#error</c> lines; the analyzer reports
/// the matching diagnostics. <see cref="InternalError"/> is the only problem the generator itself reports (GSH0048).
/// The result holds values only, so a result taken from the cache never refers to an earlier compilation.
/// </remarks>
/// <param name="Class">The class that the kernel's host members go into.</param>
/// <param name="Host">The kernel's host launch ABI.</param>
/// <param name="CudaSource">The CUDA source of the kernel.</param>
/// <param name="CppSource">The C++ source of the kernel.</param>
/// <param name="ManagedCSharpSource">The managed fallback of the kernel.</param>
/// <param name="CudaIncludes">The <c>#include</c> targets of the CUDA source.</param>
/// <param name="CppIncludes">The <c>#include</c> targets of the C++ source.</param>
/// <param name="InternalError">The exception that stopped the transpilation (GSH0048), or empty.</param>
/// <param name="StructExports">The device code export records of the structs that the kernel uses.</param>
internal readonly record struct TranspiledKernelResult(
    HostClassHeader Class,
    KernelHostModel Host,
    string CudaSource,
    string CppSource,
    string ManagedCSharpSource,
    EquatableArray<string> CudaIncludes,
    EquatableArray<string> CppIncludes,
    string InternalError,
    EquatableArray<DeviceExportRecord> StructExports = default)
{
    public string GetSource(bool isCuda) => isCuda ? CudaSource : CppSource;

    public EquatableArray<string> GetIncludes(bool isCuda) => isCuda ? CudaIncludes : CppIncludes;
}
