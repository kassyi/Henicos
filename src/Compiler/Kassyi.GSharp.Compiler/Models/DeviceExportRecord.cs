namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// One <c>[assembly: GpuDeviceExport]</c> record: the transpiled text of a device function or struct, with what a
/// consuming assembly needs to place it (names, flags, the names it refers to). Values only, so the pipeline caches it.
/// </summary>
/// <remarks>The members mirror <c>Kassyi.GSharp.Core.GpuDeviceExportAttribute</c>; see there for each one.</remarks>
internal readonly record struct DeviceExportRecord(
    string Kind,
    string Id,
    string CppName,
    string CppFullName,
    EquatableArray<string> DefinedNames,
    EquatableArray<string> DefinedIds,
    EquatableArray<string> DefinedFullNames,
    EquatableArray<string> CudaReferences,
    EquatableArray<string> CppReferences,
    string FrameName,
    EquatableArray<string> FrameDependencies,
    bool Suspends,
    bool ReachesBlockBarrier,
    bool IsParallelDependent,
    bool ReachesDynamicSharedMemory,
    EquatableArray<string> DynamicSharedMemorySites,
    string Effect,
    string CudaSignature,
    string CudaBody,
    string CudaDispose,
    string CppSignature,
    string CppBody,
    string CppDispose,
    EquatableArray<string> CudaIncludes,
    EquatableArray<string> CppIncludes,
    bool CudaFailed,
    bool CppFailed,
    bool ManagedStateMachine,
    bool ManagedVersion,
    string InternalError)
{
    /// <summary>The kind of a device function record.</summary>
    public const string FunctionKind = "Function";

    /// <summary>The kind of a struct record.</summary>
    public const string StructKind = "Struct";

    /// <summary>The version of the record layout (<c>GpuDeviceExportAttribute.FormatVersion</c>).</summary>
    /// <remarks>
    /// Raise it with every change of the record layout, and with every change of the rules that spell the names a text
    /// refers to (<c>GSharpNameEncoding.Encode</c>, <c>GlobalCppNames.Shorten</c>, the overload ordinals): a consumer
    /// finds the records by those names. A change of <c>InternalNames.yml</c> or of a prelude changes
    /// <see cref="CurrentPreludeHash"/> by itself.
    /// </remarks>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// The hash that this G# writes into its records and requires of the records it imports
    /// (<c>GpuDeviceExportAttribute.PreludeHash</c>): the preludes and the internal names
    /// (<see cref="GSharpPreludeResources.Hash"/>) with <see cref="CurrentFormatVersion"/>.
    /// </summary>
    /// <remarks>
    /// [WHY] The text of a record is only valid for the prelude it was written for and for the spelling of the
    /// generated names (an imported text calls the prelude's helpers and names the library's functions by that
    /// spelling); a new layout of the record also changes it. The format version alone would miss a change of the prelude
    /// or of the internal names that keeps the layout.
    /// </remarks>
    public static readonly string CurrentPreludeHash =
        HashHelper.ComputeFnv1A64($"{GSharpPreludeResources.Hash}/{CurrentFormatVersion}")
            .ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>The global C++ names that the text of a dialect refers to and does not define itself.</summary>
    public EquatableArray<string> GetReferences(bool isCuda) => isCuda ? CudaReferences : CppReferences;

    /// <summary>Whether the record holds a value: a default record has no ID.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Id);
}

/// <summary>The device code export records of one compilation, and the generator's own failures in building them.</summary>
/// <param name="Functions">The records of the device functions.</param>
/// <param name="Structs">The records of the structs, one per struct and method that uses it (written once).</param>
internal readonly record struct DeviceExportSet(
    EquatableArray<DeviceExportRecord> Functions,
    EquatableArray<DeviceExportRecord> Structs);
