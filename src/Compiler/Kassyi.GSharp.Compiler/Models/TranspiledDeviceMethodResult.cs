using Kassyi.GSharp.Compiler.Helpers;

namespace Kassyi.GSharp.Compiler.Models;

internal readonly struct TranspiledDeviceMethodResult(
    IMethodSymbol methodSymbol,
    string cudaSignature,
    string cudaBody,
    string cppSignature,
    string cppBody,
    EquatableArray<EquatableDiagnostic> diagnostics) : IEquatable<TranspiledDeviceMethodResult>
{
    public IMethodSymbol MethodSymbol { get; } = methodSymbol;
    public string CudaSignature { get; } = cudaSignature;
    public string CudaBody { get; } = cudaBody;
    public string CppSignature { get; } = cppSignature;
    public string CppBody { get; } = cppBody;
    public EquatableArray<EquatableDiagnostic> Diagnostics { get; } = diagnostics;

    public bool Equals(TranspiledDeviceMethodResult other)
    {
        return CudaSignature == other.CudaSignature &&
               CudaBody == other.CudaBody &&
               CppSignature == other.CppSignature &&
               CppBody == other.CppBody &&
               Diagnostics.Equals(other.Diagnostics);
    }

    public override bool Equals(object? obj) => obj is TranspiledDeviceMethodResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(CudaSignature);
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(CudaBody);
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(CppSignature);
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(CppBody);
        hash = hash * 31 + Diagnostics.GetHashCode();
        return hash;
    }

    public static bool operator ==(TranspiledDeviceMethodResult left, TranspiledDeviceMethodResult right) =>
        left.Equals(right);

    public static bool operator !=(TranspiledDeviceMethodResult left, TranspiledDeviceMethodResult right) =>
        !left.Equals(right);
}
