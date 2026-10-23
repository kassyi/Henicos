namespace Kassyi.GSharp.Compiler.Models;

internal readonly struct TranspiledDeviceMethodResult(
    IMethodSymbol methodSymbol,
    string methodName,
    string className,
    string? @namespace,
    string cudaSignature,
    string cudaBody,
    string cppSignature,
    string cppBody,
    EquatableArray<string> requiredIncludes,
    EquatableArray<EquatableDiagnostic> diagnostics,
    EquatableArray<EquatableDiagnostic> cppOnlyDiagnostics,
    EquatableArray<string> cudaFailures,
    EquatableArray<string> cppFailures,
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
    public EquatableArray<string> RequiredIncludes { get; } = requiredIncludes;
    /// <summary>The diagnostics of the CUDA transpilation, reported for every dialect.</summary>
    public EquatableArray<EquatableDiagnostic> Diagnostics { get; } = diagnostics;

    /// <summary>The diagnostics that only the C++ transpilation reports (e.g. GSH0054), reported for the C++ dialects.</summary>
    public EquatableArray<EquatableDiagnostic> CppOnlyDiagnostics { get; } = cppOnlyDiagnostics;

    /// <summary>Why the CUDA transpilation could not emit parts of the function (<see cref="TranspileFailure"/>).</summary>
    public EquatableArray<string> CudaFailures { get; } = cudaFailures;

    /// <summary>Why the C++ transpilation could not emit parts of the function (<see cref="TranspileFailure"/>).</summary>
    public EquatableArray<string> CppFailures { get; } = cppFailures;

    public EquatableArray<string> GetFailures(bool isCuda) => isCuda ? CudaFailures : CppFailures;

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
               RequiredIncludes.Equals(other.RequiredIncludes) &&
               Diagnostics.Equals(other.Diagnostics) &&
               CppOnlyDiagnostics.Equals(other.CppOnlyDiagnostics) &&
               CudaFailures.Equals(other.CudaFailures) &&
               CppFailures.Equals(other.CppFailures) &&
               FrameName == other.FrameName &&
               FrameDependencies.Equals(other.FrameDependencies) &&
               ManagedCSharpSource == other.ManagedCSharpSource;
    }

    public override bool Equals(object? obj) => obj is TranspiledDeviceMethodResult other && Equals(other);

    public override int GetHashCode() => HashHelper.Combine(MethodName, ClassName, Namespace, CudaSignature, CudaBody,
        CppSignature, CppBody, RequiredIncludes, Diagnostics, CppOnlyDiagnostics, CudaFailures, CppFailures, FrameName,
        ManagedCSharpSource);

    public static bool operator ==(TranspiledDeviceMethodResult left, TranspiledDeviceMethodResult right) =>
        left.Equals(right);

    public static bool operator !=(TranspiledDeviceMethodResult left, TranspiledDeviceMethodResult right) =>
        !left.Equals(right);
}
