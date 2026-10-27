namespace Kassyi.GSharp.Compiler.Models;

/// <summary>The transpiled outputs of one <c>[GpuDeviceFunction]</c>, for both native dialects.</summary>
/// <remarks>
/// A construct that a dialect could not emit turns that dialect's body into <c>#error</c> lines; the analyzer reports the
/// matching diagnostics. <see cref="InternalError"/> is the only problem the generator itself reports (GSH0048).
/// The result holds values only, so a result taken from the cache never refers to an earlier compilation.
/// </remarks>
/// <param name="Class">The class that the function's managed state machine goes into.</param>
/// <param name="MethodName">The function name.</param>
/// <param name="CudaSignature">The CUDA declaration.</param>
/// <param name="CudaBody">The CUDA definition.</param>
/// <param name="CppSignature">The C++ declaration.</param>
/// <param name="CppBody">The C++ definition.</param>
/// <param name="CudaIncludes">The <c>#include</c> targets of the CUDA body.</param>
/// <param name="CppIncludes">The <c>#include</c> targets of the C++ body.</param>
/// <param name="InternalError">The exception that stopped the transpilation (GSH0048), or empty.</param>
/// <param name="FrameName">The C++ frame type of a device function that may suspend, or empty for any other device function.</param>
/// <param name="FrameDependencies">The frame types that this function's frame contains; they are defined before it.</param>
/// <param name="ManagedCSharpSource">The managed state machine of a device function that may suspend, or empty.</param>
/// <param name="Export">The device code export record of the function, or default when it is not exported (an iterator).</param>
/// <param name="StructExports">The device code export records of the structs that the function uses.</param>
internal readonly record struct TranspiledDeviceMethodResult(
    HostClassHeader Class,
    string MethodName,
    string CudaSignature,
    string CudaBody,
    string CppSignature,
    string CppBody,
    EquatableArray<string> CudaIncludes,
    EquatableArray<string> CppIncludes,
    string InternalError,
    string FrameName = "",
    EquatableArray<string> FrameDependencies = default,
    string ManagedCSharpSource = "",
    DeviceExportRecord Export = default,
    EquatableArray<DeviceExportRecord> StructExports = default)
{
    public string GetSignature(bool isCuda) => isCuda ? CudaSignature : CppSignature;

    public string GetBody(bool isCuda) => isCuda ? CudaBody : CppBody;

    public EquatableArray<string> GetIncludes(bool isCuda) => isCuda ? CudaIncludes : CppIncludes;
}
