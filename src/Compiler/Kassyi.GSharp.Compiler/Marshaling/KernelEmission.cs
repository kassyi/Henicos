using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>One kernel of a <see cref="KernelClassModel"/>, with what its siblings decide.</summary>
/// <param name="Host">The kernel's own launch ABI.</param>
/// <param name="MemberKey">The suffix of the per-kernel host members: the name, or <c>{name}__o{n}</c> for an overloaded kernel.</param>
/// <param name="IsOverloaded">Whether another <c>[GpuKernel]</c> of the class has the same name.</param>
/// <param name="IsIndistinguishable">
/// Whether an earlier kernel of the class already has one of the kernel's launchers or its warmup method with the same
/// parameter types (GSH0059, <see cref="LauncherSignatures.FindConflicts"/>), so its host members are left out.
/// </param>
/// <param name="CudaSource">The CUDA source of the kernel.</param>
/// <param name="CppSource">The C++ source of the kernel.</param>
/// <param name="ManagedCSharpSource">The managed fallback of the kernel.</param>
/// <param name="InternalError">The exception that stopped the transpilation (GSH0048), or empty.</param>
/// <param name="ErrorSites">
/// The source locations of the run-time error sites of the kernel, which the class reports by site ID.
/// </param>
internal readonly record struct KernelEmission(
    KernelHostModel Host,
    string MemberKey,
    bool IsOverloaded,
    bool IsIndistinguishable,
    string CudaSource,
    string CppSource,
    string ManagedCSharpSource,
    string InternalError,
    EquatableArray<ErrorSiteEntry> ErrorSites = default)
{
    public string GetSource(bool isCuda) => isCuda ? CudaSource : CppSource;

    /// <summary>
    /// Whether the kernel has a CUDA warmup method for the module context to call: it has launchers (GSH0059, GSH0106
    /// and GSH0108 leave them out).
    /// </summary>
    public bool HasWarmup => !IsIndistinguishable && !Host.HasReservedLaunchParameterName &&
                             Host.LauncherUnavailableReason is null;
}
