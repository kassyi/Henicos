using Kassyi.GSharp.Compiler.Helpers;

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
    EquatableArray<EquatableDiagnostic> diagnostics) : IEquatable<TranspiledDeviceMethodResult>
{
    public IMethodSymbol MethodSymbol { get; } = methodSymbol;
    public string MethodName { get; } = methodName;
    public string ClassName { get; } = className;
    public string? Namespace { get; } = @namespace;
    public string CudaSignature { get; } = cudaSignature;
    public string CudaBody { get; } = cudaBody;
    public string CppSignature { get; } = cppSignature;
    public string CppBody { get; } = cppBody;
    public EquatableArray<EquatableDiagnostic> Diagnostics { get; } = diagnostics;

    public bool Equals(TranspiledDeviceMethodResult other)
    {
        return MethodName == other.MethodName &&
               ClassName == other.ClassName &&
               Namespace == other.Namespace &&
               CudaSignature == other.CudaSignature &&
               CudaBody == other.CudaBody &&
               CppSignature == other.CppSignature &&
               CppBody == other.CppBody &&
               Diagnostics.Equals(other.Diagnostics);
    }

    public override bool Equals(object? obj) => obj is TranspiledDeviceMethodResult other && Equals(other);

    public override int GetHashCode() => HashHelper.Combine(MethodName, ClassName, Namespace, CudaSignature, CudaBody,
        CppSignature, CppBody, Diagnostics);

    public static bool operator ==(TranspiledDeviceMethodResult left, TranspiledDeviceMethodResult right) =>
        left.Equals(right);

    public static bool operator !=(TranspiledDeviceMethodResult left, TranspiledDeviceMethodResult right) =>
        !left.Equals(right);
}
