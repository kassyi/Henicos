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
        bool Failed);

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
        bool HasExitOnlyBarrier = false);

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
    public const int CurrentFormatVersion = 2;

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
    public EquatableArray<string> GetReferences(bool isCuda) => (isCuda ? Cuda : Cpp).References;

    /// <summary>Whether the record holds a value: a default record has no ID.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Id);

    /// <summary>The short C++ name of the declaration.</summary>
    public string CppName => Names.CppName;

    /// <summary>The full C++ name of the declaration.</summary>
    public string CppFullName => Names.CppFullName;

    /// <summary>The short C++ names of the functions a struct defines.</summary>
    public EquatableArray<string> DefinedNames => Names.DefinedNames;

    /// <summary>The documentation comment IDs of the functions a struct defines.</summary>
    public EquatableArray<string> DefinedIds => Names.DefinedIds;

    /// <summary>The full C++ names of the functions a struct defines.</summary>
    public EquatableArray<string> DefinedFullNames => Names.DefinedFullNames;

    /// <summary>The C++ names that the CUDA text refers to.</summary>
    public EquatableArray<string> CudaReferences => Cuda.References;

    /// <summary>The C++ names that the C++ text refers to.</summary>
    public EquatableArray<string> CppReferences => Cpp.References;

    /// <summary>The frame struct name of a suspending function.</summary>
    public string FrameName => Traits.FrameName;

    /// <summary>The frames that the frame struct embeds.</summary>
    public EquatableArray<string> FrameDependencies => Traits.FrameDependencies;

    /// <summary>Whether the function reaches a phase barrier.</summary>
    public bool Suspends => Traits.Suspends;

    /// <summary>Whether the function reaches a block barrier.</summary>
    public bool ReachesBlockBarrier => Traits.ReachesBlockBarrier;

    /// <summary>Whether the function requires parallel execution.</summary>
    public bool IsParallelDependent => Traits.IsParallelDependent;

    /// <summary>Whether the function reaches dynamic shared memory.</summary>
    public bool ReachesDynamicSharedMemory => Traits.ReachesDynamicSharedMemory;

    /// <summary>The allocation sites of dynamic shared memory.</summary>
    public EquatableArray<string> DynamicSharedMemorySites => Traits.DynamicSharedMemorySites;

    /// <summary>The side-effect classification of the function.</summary>
    public string Effect => Traits.Effect;

    /// <summary>The CUDA signature of the function.</summary>
    public string CudaSignature => Cuda.Signature;

    /// <summary>The CUDA body of the function or definition of the struct.</summary>
    public string CudaBody => Cuda.Body;

    /// <summary>The CUDA dispose function text of the struct.</summary>
    public string CudaDispose => Cuda.Dispose;

    /// <summary>The C++ signature of the function.</summary>
    public string CppSignature => Cpp.Signature;

    /// <summary>The C++ body of the function or definition of the struct.</summary>
    public string CppBody => Cpp.Body;

    /// <summary>The C++ dispose function text of the struct.</summary>
    public string CppDispose => Cpp.Dispose;

    /// <summary>The headers the CUDA text includes.</summary>
    public EquatableArray<string> CudaIncludes => Cuda.Includes;

    /// <summary>The headers the C++ text includes.</summary>
    public EquatableArray<string> CppIncludes => Cpp.Includes;

    /// <summary>Whether transpilation failed for CUDA.</summary>
    public bool CudaFailed => Cuda.Failed;

    /// <summary>Whether transpilation failed for C++.</summary>
    public bool CppFailed => Cpp.Failed;

    /// <summary>Whether the function has an emitted managed state machine.</summary>
    public bool ManagedStateMachine => Traits.ManagedStateMachine;

    /// <summary>Whether the function has an emitted managed version.</summary>
    public bool ManagedVersion => Traits.ManagedVersion;

    /// <summary>The parameter bits that make a barrier condition diverge.</summary>
    public int DivergentConditionParameters => Traits.DivergentConditionParameters;

    /// <summary>The parameter bits that make a barrier exit diverge.</summary>
    public int DivergentExitOnlyParameters => Traits.DivergentExitOnlyParameters;

    /// <summary>Whether the function reaches an exit-only barrier.</summary>
    public bool HasExitOnlyBarrier => Traits.HasExitOnlyBarrier;
}

/// <summary>The device code export records of one compilation, and the generator's own failures in building them.</summary>
/// <param name="Functions">The records of the device functions.</param>
/// <param name="Structs">The records of the structs, one per struct and method that uses it (written once).</param>
internal readonly record struct DeviceExportSet(
    EquatableArray<DeviceExportRecord> Functions,
    EquatableArray<DeviceExportRecord> Structs);
