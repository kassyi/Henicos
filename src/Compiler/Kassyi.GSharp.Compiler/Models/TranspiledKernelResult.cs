using Kassyi.GSharp.Compiler.Aot;

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
/// <param name="ErrorSites">The error site entries registered for this kernel.</param>
/// <param name="HeldRefLocalSites">
/// The ref locals that the CPU state machine would keep in its frame and GSH0039 does not report; the generator reports
/// GSH0003 at each on a CPU dialect build (#1640).
/// </param>
/// <param name="ManagedStackSource">
/// The unlowered stack form of the kernel for the managed stack model, or empty; attached after the transpilation
/// under <c>GSharpManagedBarrierModel=Stack</c> only (<see cref="Transpiler.ManagedStackFormCollector"/>).
/// </param>
/// <param name="Spill">
/// What the spill warning (GS0001) of the kernel covers (<see cref="SpillSuppressionPlan"/>).
/// </param>
/// <param name="NativeFallback">
/// Why the native CPU path keeps the method on its state machine under <c>GSharpNativeBarrierModel=Stack</c>, for the
/// fallback list (<see cref="StateMachineFallbackPipeline"/>); default when it runs on the stack.
/// </param>
/// <param name="RunsOnNativeStack">
/// Whether <see cref="NativeStackTranslator.ForKernel"/> emitted the native stack form for this kernel.
/// </param>
internal readonly record struct TranspiledKernelResult(
    HostClassHeader Class,
    KernelHostModel Host,
    string CudaSource,
    string CppSource,
    string ManagedCSharpSource,
    EquatableArray<string> CudaIncludes,
    EquatableArray<string> CppIncludes,
    string InternalError,
    EquatableArray<DeviceExportRecord> StructExports = default,
    EquatableArray<ErrorSiteEntry> ErrorSites = default,
    EquatableArray<HeldRefLocalSite> HeldRefLocalSites = default,
    string ManagedStackSource = "",
    KernelSpillScope Spill = default,
    StateMachineFallback NativeFallback = default,
    bool RunsOnNativeStack = false)
{
    public string GetSource(bool isCuda) => isCuda ? CudaSource : CppSource;

    public EquatableArray<string> GetIncludes(bool isCuda) => isCuda ? CudaIncludes : CppIncludes;
}
