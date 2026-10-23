namespace Kassyi.GSharp.Compiler.Models;

/// <summary>The transpiled outputs of one <c>[GpuDeviceFunction]</c>, for both native dialects.</summary>
/// <remarks>
/// A construct that a dialect could not emit turns that dialect's body into <c>#error</c> lines; the analyzer reports the
/// matching diagnostics. <see cref="InternalError"/> is the only problem the generator itself reports (GSH0048).
/// </remarks>
internal readonly struct TranspiledDeviceMethodResult(
    IMethodSymbol methodSymbol,
    string methodName,
    string className,
    string? @namespace,
    string cudaSignature,
    string cudaBody,
    string cppSignature,
    string cppBody,
    EquatableArray<string> cudaIncludes,
    EquatableArray<string> cppIncludes,
    string internalError,
    string frameName = "",
    EquatableArray<string> frameDependencies = default,
    string managedCSharpSource = "") : IEquatable<TranspiledDeviceMethodResult>
{
    public IMethodSymbol MethodSymbol { get; } = methodSymbol;
    public string MethodName { get; } = methodName;
    public string ClassName { get; } = className;
    public string? Namespace { get; } = @namespace;
    public string CudaSignature { get; } = cudaSignature;
    public string CudaBody { get; } = cudaBody;
    public string CppSignature { get; } = cppSignature;
    public string CppBody { get; } = cppBody;
    public string GetSignature(bool isCuda) => isCuda ? CudaSignature : CppSignature;
    public string GetBody(bool isCuda) => isCuda ? CudaBody : CppBody;

    /// <summary>The <c>#include</c> targets of the CUDA body.</summary>
    public EquatableArray<string> CudaIncludes { get; } = cudaIncludes;

    /// <summary>The <c>#include</c> targets of the C++ body.</summary>
    public EquatableArray<string> CppIncludes { get; } = cppIncludes;

    public EquatableArray<string> GetIncludes(bool isCuda) => isCuda ? CudaIncludes : CppIncludes;

    /// <summary>The exception that stopped the transpilation (GSH0048), or empty.</summary>
    public string InternalError { get; } = internalError;

    /// <summary>The C++ frame type of a device function that may suspend, or empty for any other device function.</summary>
    public string FrameName { get; } = frameName;

    /// <summary>The frame types that this function's frame contains; they are defined before it.</summary>
    public EquatableArray<string> FrameDependencies { get; } = frameDependencies;

    /// <summary>The managed state machine of a device function that may suspend, or empty.</summary>
    public string ManagedCSharpSource { get; } = managedCSharpSource;

    public bool Equals(TranspiledDeviceMethodResult other)
    {
        return MethodName == other.MethodName &&
               ClassName == other.ClassName &&
               Namespace == other.Namespace &&
               CudaSignature == other.CudaSignature &&
               CudaBody == other.CudaBody &&
               CppSignature == other.CppSignature &&
               CppBody == other.CppBody &&
               CudaIncludes.Equals(other.CudaIncludes) &&
               CppIncludes.Equals(other.CppIncludes) &&
               InternalError == other.InternalError &&
               FrameName == other.FrameName &&
               FrameDependencies.Equals(other.FrameDependencies) &&
               ManagedCSharpSource == other.ManagedCSharpSource;
    }

    public override bool Equals(object? obj) => obj is TranspiledDeviceMethodResult other && Equals(other);

    public override int GetHashCode() => HashHelper.Combine(MethodName, ClassName, Namespace, CudaSignature, CudaBody,
        CppSignature, CppBody, CudaIncludes, CppIncludes, InternalError, FrameName, ManagedCSharpSource);

    public static bool operator ==(TranspiledDeviceMethodResult left, TranspiledDeviceMethodResult right) =>
        left.Equals(right);

    public static bool operator !=(TranspiledDeviceMethodResult left, TranspiledDeviceMethodResult right) =>
        !left.Equals(right);
}
