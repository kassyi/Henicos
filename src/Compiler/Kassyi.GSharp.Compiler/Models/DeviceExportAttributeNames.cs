namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// The named arguments of <c>Kassyi.GSharp.Core.GpuDeviceExportAttribute</c>: the names that
/// <c>DeviceExportEmitter</c> writes and <c>DeviceExportTable</c> reads back, kept in one place so the two sides cannot
/// drift apart.
/// </summary>
/// <remarks>
/// Each constant is the name of a property of the attribute; <c>DeviceExportAttributeNamesTests</c> checks that every
/// constant names one, and that every settable property of the attribute has a constant.
/// </remarks>
internal static class DeviceExportAttributeNames
{
    public const string CppName = "CppName";
    public const string CppFullName = "CppFullName";
    public const string DefinedNames = "DefinedNames";
    public const string DefinedIds = "DefinedIds";
    public const string DefinedFullNames = "DefinedFullNames";
    public const string CudaReferences = "CudaReferences";
    public const string CppReferences = "CppReferences";
    public const string FrameName = "FrameName";
    public const string FrameDependencies = "FrameDependencies";
    public const string Suspends = "Suspends";
    public const string ReachesBlockBarrier = "ReachesBlockBarrier";
    public const string IsParallelDependent = "IsParallelDependent";
    public const string ReachesDynamicSharedMemory = "ReachesDynamicSharedMemory";
    public const string DynamicSharedMemorySites = "DynamicSharedMemorySites";
    public const string Effect = "Effect";
    public const string CudaSignature = "CudaSignature";
    public const string CudaBody = "CudaBody";
    public const string CudaDispose = "CudaDispose";
    public const string CppSignature = "CppSignature";
    public const string CppBody = "CppBody";
    public const string CppDispose = "CppDispose";
    public const string CudaIncludes = "CudaIncludes";
    public const string CppIncludes = "CppIncludes";
    public const string CudaFailed = "CudaFailed";
    public const string CppFailed = "CppFailed";
    public const string InternalError = "InternalError";
    public const string ManagedStateMachine = "ManagedStateMachine";
    public const string ManagedVersion = "ManagedVersion";
    public const string DivergentConditionParameters = "DivergentConditionParameters";
    public const string DivergentExitOnlyParameters = "DivergentExitOnlyParameters";
    public const string HasExitOnlyBarrier = "HasExitOnlyBarrier";

    /// <summary>The names of the CUDA text of a record (<see cref="DeviceExportRecord.Cuda"/>).</summary>
    public static readonly DialectNames Cuda =
        new(CudaReferences, CudaSignature, CudaBody, CudaDispose, CudaIncludes, CudaFailed);

    /// <summary>The names of the C++ text of a record (<see cref="DeviceExportRecord.Cpp"/>).</summary>
    public static readonly DialectNames Cpp =
        new(CppReferences, CppSignature, CppBody, CppDispose, CppIncludes, CppFailed);

    /// <summary>The names of the CUDA or the C++ text.</summary>
    public static DialectNames ForDialect(bool isCuda) => isCuda ? Cuda : Cpp;

    /// <summary>The named arguments of one dialect's <see cref="DeviceExportRecord.DialectText"/>.</summary>
    public readonly record struct DialectNames(
        string References,
        string Signature,
        string Body,
        string Dispose,
        string Includes,
        string Failed);
}
