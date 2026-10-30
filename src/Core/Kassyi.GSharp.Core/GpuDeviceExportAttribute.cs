namespace Kassyi.GSharp.Core;

/// <summary>
/// Records the transpiled device code of one <c>[GpuDeviceFunction]</c> or struct of the assembly, so that a kernel in
/// another assembly that calls it can place the same C++ in its own native module.
/// </summary>
/// <remarks>
/// <para>
/// The G# source generator writes one of these per device function and struct (<c>GSharpSharedDeviceExports.g.cs</c>);
/// user code never writes it. A consuming compilation reads the records from the referenced assembly's metadata, without
/// file I/O, and copies only the records that its kernels reach into its own <c>.g.cu</c> / <c>.g.cpp</c>, so the
/// native compiler sees one translation unit and inlines across the assembly boundary.
/// </para>
/// <para>
/// The record holds text only. <see cref="PreludeHash"/> and <see cref="FormatVersion"/> must match the consuming
/// compiler, which reports a mismatch instead of compiling text written for another prelude.
/// Set the MSBuild property <c>GSharpExportDeviceCode</c> to <c>false</c> to stop the generator from writing them.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[ExcludeFromCodeCoverage]
// ReSharper disable UnusedMember.Global, UnusedAutoPropertyAccessor.Global -- Written by the generator, read from metadata
public sealed class GpuDeviceExportAttribute : Attribute
{
    /// <summary>Initializes a record.</summary>
    /// <param name="formatVersion">The version of the record layout.</param>
    /// <param name="preludeHash">
    /// The hash of what the text was written for: the CUDA and C++ preludes, the internal names and the record layout.
    /// </param>
    /// <param name="kind"><c>Function</c> or <c>Struct</c>.</param>
    /// <param name="id">The documentation comment ID of the function or struct.</param>
    public GpuDeviceExportAttribute(int formatVersion, string preludeHash, string kind, string id)
    {
        FormatVersion = formatVersion;
        PreludeHash = preludeHash;
        Kind = kind;
        Id = id;
    }

    /// <summary>Gets the version of the record layout.</summary>
    public int FormatVersion { get; }

    /// <summary>
    /// Gets the FNV-1a 64 hash of what the text was written for: the CUDA and C++ preludes and the internal names
    /// (<c>InternalNames.yml</c>, the spelling of the generated names), with the record layout.
    /// </summary>
    public string PreludeHash { get; }

    /// <summary>Gets the kind of the record: <c>Function</c> or <c>Struct</c>.</summary>
    public string Kind { get; }

    /// <summary>Gets the documentation comment ID of the function or struct.</summary>
    public string Id { get; }

    /// <summary>Gets or sets the C++ name (<c>__gsharp_fn_…</c> / <c>__gsharp_ty_…</c>), shortened when it is long.</summary>
    public string CppName { get; set; } = "";

    /// <summary>Gets or sets the C++ name before shortening, or an empty string when it was not shortened.</summary>
    public string CppFullName { get; set; } = "";

    /// <summary>Gets or sets the other C++ names the record defines: the constructors of a struct.</summary>
    public string[] DefinedNames { get; set; } = [];

    /// <summary>Gets or sets the documentation comment IDs of <see cref="DefinedNames"/>, in the same order.</summary>
    public string[] DefinedIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the global C++ names (functions, types, constructors) that the CUDA text refers to and does not
    /// define itself.
    /// </summary>
    public string[] CudaReferences { get; set; } = [];

    /// <summary>Gets or sets the global C++ names that the C++ text refers to and does not define itself.</summary>
    public string[] CppReferences { get; set; } = [];

    /// <summary>
    /// Gets or sets the names of <see cref="DefinedNames"/> before shortening, in the same order; an empty string for a
    /// name that was not shortened.
    /// </summary>
    public string[] DefinedFullNames { get; set; } = [];

    /// <summary>Gets or sets the frame type of a device function that suspends at a barrier on the CPU dialects.</summary>
    public string FrameName { get; set; } = "";

    /// <summary>Gets or sets the frame types that the frame contains by value.</summary>
    public string[] FrameDependencies { get; set; } = [];

    /// <summary>Gets or sets whether the device function reaches a phase barrier (it suspends on the CPU dialects).</summary>
    public bool Suspends { get; set; }

    /// <summary>Gets or sets whether the device function reaches a block barrier.</summary>
    public bool ReachesBlockBarrier { get; set; }

    /// <summary>
    /// Gets or sets whether a call of the device function depends on parallel execution: it reaches
    /// <c>Gpu.GridStrideLoop</c> or <c>Gpu.SyncGrid</c>, or the thread or block index, so it cannot run in a
    /// single-thread context (GSH0035).
    /// </summary>
    public bool IsParallelDependent { get; set; }

    /// <summary>Gets or sets whether the device function reaches a dynamic <c>SharedMemory&lt;T&gt;</c> declaration.</summary>
    public bool ReachesDynamicSharedMemory { get; set; }

    /// <summary>Gets or sets the site keys (16 hexadecimal digits) of the dynamic declarations that the function reaches.</summary>
    public string[] DynamicSharedMemorySites { get; set; } = [];

    /// <summary>Gets or sets what a call may write: <c>None</c>, <c>RefParameters</c> or <c>Memory</c>.</summary>
    public string Effect { get; set; } = "Memory";

    /// <summary>Gets or sets the CUDA forward declaration of a function.</summary>
    public string CudaSignature { get; set; } = "";

    /// <summary>Gets or sets the CUDA definition.</summary>
    public string CudaBody { get; set; } = "";

    /// <summary>Gets or sets the CUDA <c>Dispose</c> definition of a struct.</summary>
    public string CudaDispose { get; set; } = "";

    /// <summary>Gets or sets the C++ forward declaration of a function.</summary>
    public string CppSignature { get; set; } = "";

    /// <summary>Gets or sets the C++ definition.</summary>
    public string CppBody { get; set; } = "";

    /// <summary>Gets or sets the C++ <c>Dispose</c> definition of a struct.</summary>
    public string CppDispose { get; set; } = "";

    /// <summary>Gets or sets the <c>#include</c> targets of the CUDA text.</summary>
    public string[] CudaIncludes { get; set; } = [];

    /// <summary>Gets or sets the <c>#include</c> targets of the C++ text.</summary>
    public string[] CppIncludes { get; set; } = [];

    /// <summary>Gets or sets whether the CUDA text could not be transpiled (it holds <c>#error</c> lines).</summary>
    public bool CudaFailed { get; set; }

    /// <summary>Gets or sets whether the C++ text could not be transpiled (it holds <c>#error</c> lines).</summary>
    public bool CppFailed { get; set; }

    /// <summary>
    /// Gets or sets the message of the exception that stopped the generator from writing the text of a failed record,
    /// or an empty string; a consumer that uses it states the message.
    /// </summary>
    public string InternalError { get; set; } = "";

    /// <summary>Gets or sets whether the assembly holds the managed state machine of a suspending function.</summary>
    public bool ManagedStateMachine { get; set; }

    /// <summary>
    /// Gets or sets the parameters, one bit per ordinal (past the 32nd sharing the last bit), whose thread-variant
    /// argument makes only some threads reach a barrier of the device function, where uniform arguments do not (GSH0083
    /// at the call site).
    /// </summary>
    public int DivergentConditionParameters { get; set; }

    /// <summary>
    /// Gets or sets the parameters, one bit per ordinal, whose thread-variant argument makes some threads leave the
    /// device function early before a barrier, where uniform arguments do not.
    /// </summary>
    public int DivergentExitOnlyParameters { get; set; }

    /// <summary>
    /// Gets or sets whether some threads may leave the device function early before a barrier even with uniform
    /// arguments; a barrier of the caller after the call then diverges.
    /// </summary>
    public bool HasExitOnlyBarrier { get; set; }
}
