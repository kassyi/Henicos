namespace Kassyi.GSharp.Compiler.Models;

/// <summary>
/// The named arguments of <c>Kassyi.GSharp.Core.GpuDeviceExportAttribute</c>: the names that
/// <c>DeviceExportEmitter</c> writes and <c>DeviceExportTable</c> reads back, kept in one place so the two sides cannot
/// drift apart. Grouped as <see cref="DeviceExportRecord"/> groups its members.
/// </summary>
/// <remarks>
/// Each name is the name of a property of the attribute; <c>DeviceExportTests</c> checks that the names here are
/// exactly the settable properties of the attribute.
/// </remarks>
internal static class DeviceExportAttributeNames
{
    /// <summary>The name of <see cref="DeviceExportRecord.InternalError"/>.</summary>
    public const string InternalError = "InternalError";

    /// <summary>The names of the CUDA text of a record (<see cref="DeviceExportRecord.Cuda"/>).</summary>
    public static readonly DialectNames Cuda = new(
        References: "CudaReferences",
        Signature: "CudaSignature",
        Body: "CudaBody",
        Dispose: "CudaDispose",
        Includes: "CudaIncludes",
        Failed: "CudaFailed");

    /// <summary>The names of the C++ text of a record (<see cref="DeviceExportRecord.Cpp"/>).</summary>
    public static readonly DialectNames Cpp = new(
        References: "CppReferences",
        Signature: "CppSignature",
        Body: "CppBody",
        Dispose: "CppDispose",
        Includes: "CppIncludes",
        Failed: "CppFailed");

    /// <summary>The names of <see cref="DeviceExportRecord.ExportNames"/>.</summary>
    public static class Names
    {
        public const string CppName = "CppName";
        public const string CppFullName = "CppFullName";
        public const string DefinedNames = "DefinedNames";
        public const string DefinedIds = "DefinedIds";
        public const string DefinedFullNames = "DefinedFullNames";
    }

    /// <summary>The names of <see cref="DeviceExportRecord.FunctionTraits"/>.</summary>
    public static class Traits
    {
        public const string FrameName = "FrameName";
        public const string FrameDependencies = "FrameDependencies";
        public const string Suspends = "Suspends";
        public const string ReachesBlockBarrier = "ReachesBlockBarrier";
        public const string IsParallelDependent = "IsParallelDependent";
        public const string ReachesDynamicSharedMemory = "ReachesDynamicSharedMemory";
        public const string DynamicSharedMemorySites = "DynamicSharedMemorySites";
        public const string Effect = "Effect";
        public const string ManagedStateMachine = "ManagedStateMachine";
        public const string ManagedVersion = "ManagedVersion";
        public const string DivergentConditionParameters = "DivergentConditionParameters";
        public const string DivergentExitOnlyParameters = "DivergentExitOnlyParameters";
        public const string HasExitOnlyBarrier = "HasExitOnlyBarrier";
        public const string ReachesArrivalBarrier = "ReachesArrivalBarrier";
    }

    /// <summary>The names of one dialect's <see cref="DeviceExportRecord.DialectText"/>.</summary>
    public readonly record struct DialectNames(
        string References,
        string Signature,
        string Body,
        string Dispose,
        string Includes,
        string Failed);
}
