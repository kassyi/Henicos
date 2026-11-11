namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// One <c>[assembly: GpuDeviceExport]</c> record: the transpiled text of a device function or struct, with what a
/// consuming assembly needs to place it (names, flags, the names it refers to). Values only, so the pipeline caches it.
/// </summary>
/// <remarks>The members mirror <c>Kassyi.GSharp.Core.GpuDeviceExportAttribute</c>; see there for each one.</remarks>
internal readonly record struct DeviceExportRecord(
    string Kind,
    string Id,
    DeviceExportRecord.ExportNames Names,
    DeviceExportRecord.DialectText Cuda,
    DeviceExportRecord.DialectText Cpp,
    DeviceExportRecord.FunctionTraits Traits,
    string InternalError)
{
    /// <summary>The names the record defines and refers to.</summary>
    public readonly record struct ExportNames(
        string CppName,
        string CppFullName,
        EquatableArray<string> DefinedNames,
        EquatableArray<string> DefinedIds,
        EquatableArray<string> DefinedFullNames);

    /// <summary>The text and dependencies of one dialect.</summary>
    public readonly record struct DialectText(
        EquatableArray<string> References,
        string Signature,
        string Body,
        string Dispose,
        EquatableArray<string> Includes,
        bool Failed)
    {
        /// <summary>No text and no references, not failed.</summary>
        public static DialectText Empty => new(
            References: default,
            Signature: "",
            Body: "",
            Dispose: "",
            Includes: default,
            Failed: false);

        /// <summary>No text, failed: a consumer copies nothing of the dialect.</summary>
        public static DialectText NotTranspiled => Empty with { Failed = true };
    }

    /// <summary>The traits and execution requirements of a device function.</summary>
    public readonly record struct FunctionTraits(
        string FrameName,
        EquatableArray<string> FrameDependencies,
        bool Suspends,
        bool ReachesBlockBarrier,
        bool IsParallelDependent,
        bool ReachesDynamicSharedMemory,
        EquatableArray<string> DynamicSharedMemorySites,
        string Effect,
        bool ManagedStateMachine,
        bool ManagedVersion,
        int DivergentConditionParameters = 0,
        int DivergentExitOnlyParameters = 0,
        bool HasExitOnlyBarrier = false,
        bool ReachesArrivalBarrier = false)
    {
        /// <summary>
        /// The traits of a record that is not a transpiled function (a struct, or a function that failed): no frame, no
        /// barrier, no dynamic shared memory, the <c>Memory</c> effect (the attribute's default).
        /// </summary>
        public static FunctionTraits None => new(
            FrameName: "",
            FrameDependencies: default,
            Suspends: false,
            ReachesBlockBarrier: false,
            IsParallelDependent: false,
            ReachesDynamicSharedMemory: false,
            DynamicSharedMemorySites: default,
            Effect: "Memory",
            ManagedStateMachine: false,
            ManagedVersion: false);
    }

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
    public const int CurrentFormatVersion = 3;

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

    /// <summary>The text of the CUDA or the C++ dialect.</summary>
    public DialectText GetDialect(bool isCuda) => isCuda ? Cuda : Cpp;

    /// <summary>Whether the record holds a value: a default record has no ID.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Id);
}

/// <summary>The device code export records of one compilation, and the generator's own failures in building them.</summary>
/// <param name="Functions">The records of the device functions.</param>
/// <param name="Structs">The records of the structs, one per struct and method that uses it (written once).</param>
internal readonly record struct DeviceExportSet(
    EquatableArray<DeviceExportRecord> Functions,
    EquatableArray<DeviceExportRecord> Structs);
