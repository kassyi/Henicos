namespace Kassyi.GSharp.Compiler.Models;

internal readonly struct TranspiledKernelResult(
    IMethodSymbol methodSymbol,
    string methodName,
    string className,
    string? @namespace,
    string cudaSource,
    string cppSource,
    string managedCSharpSource,
    EquatableArray<string> requiredIncludes,
    EquatableArray<EquatableDiagnostic> diagnostics,
    EquatableArray<EquatableDiagnostic> cppOnlyDiagnostics) : IEquatable<TranspiledKernelResult>
{
    public IMethodSymbol MethodSymbol { get; } = methodSymbol;
    public string MethodName { get; } = methodName;
    public string ClassName { get; } = className;
    public string? Namespace { get; } = @namespace;
    public string CudaSource { get; } = cudaSource;
    public string CppSource { get; } = cppSource;
    public string GetSource(bool isCuda) => isCuda ? CudaSource : CppSource;
    public string ManagedCSharpSource { get; } = managedCSharpSource;
    public EquatableArray<string> RequiredIncludes { get; } = requiredIncludes;
    /// <summary>The diagnostics of the CUDA transpilation, reported for every dialect.</summary>
    public EquatableArray<EquatableDiagnostic> Diagnostics { get; } = diagnostics;

    /// <summary>The diagnostics that only the C++ transpilation reports (e.g. GSH0054), reported for the C++ dialects.</summary>
    public EquatableArray<EquatableDiagnostic> CppOnlyDiagnostics { get; } = cppOnlyDiagnostics;

    public bool Equals(TranspiledKernelResult other)
    {
        return MethodName == other.MethodName &&
               ClassName == other.ClassName &&
               Namespace == other.Namespace &&
               CudaSource == other.CudaSource &&
               CppSource == other.CppSource &&
               ManagedCSharpSource == other.ManagedCSharpSource &&
               RequiredIncludes.Equals(other.RequiredIncludes) &&
               Diagnostics.Equals(other.Diagnostics) &&
               CppOnlyDiagnostics.Equals(other.CppOnlyDiagnostics);
    }

    public override bool Equals(object? obj) => obj is TranspiledKernelResult other && Equals(other);

    public override int GetHashCode() => HashHelper.Combine(MethodName, ClassName, Namespace, CudaSource, CppSource,
        ManagedCSharpSource, RequiredIncludes, Diagnostics, CppOnlyDiagnostics);

    public static bool operator ==(TranspiledKernelResult left, TranspiledKernelResult right) => left.Equals(right);
    public static bool operator !=(TranspiledKernelResult left, TranspiledKernelResult right) => !left.Equals(right);
}
