using Kassyi.GSharp.Compiler.Helpers;

namespace Kassyi.GSharp.Compiler.Models;

internal readonly struct TranspiledKernelResult(
    IMethodSymbol methodSymbol,
    string cudaSource,
    string cppSource,
    string? keyProvider,
    EquatableArray<EquatableDiagnostic> diagnostics) : IEquatable<TranspiledKernelResult>
{
    public IMethodSymbol MethodSymbol { get; } = methodSymbol;
    public string CudaSource { get; } = cudaSource;
    public string CppSource { get; } = cppSource;
    public string? KeyProvider { get; } = keyProvider;
    public EquatableArray<EquatableDiagnostic> Diagnostics { get; } = diagnostics;

    public bool Equals(TranspiledKernelResult other)
    {
        return CudaSource == other.CudaSource &&
               CppSource == other.CppSource &&
               KeyProvider == other.KeyProvider &&
               Diagnostics.Equals(other.Diagnostics);
    }

    public override bool Equals(object? obj) => obj is TranspiledKernelResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = 17;
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(CudaSource);
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(CppSource);
        hash = hash * 31 + HashHelper.ComputeFnv1AHash(KeyProvider);
        hash = hash * 31 + Diagnostics.GetHashCode();
        return hash;
    }

    public static bool operator ==(TranspiledKernelResult left, TranspiledKernelResult right) => left.Equals(right);
    public static bool operator !=(TranspiledKernelResult left, TranspiledKernelResult right) => !left.Equals(right);
}
