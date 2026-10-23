namespace Kassyi.GSharp.Compiler.Models;

/// <summary>The transpiled outputs of one <c>[GpuKernel]</c>, for both native dialects and the managed fallback.</summary>
/// <remarks>
/// A construct that a dialect could not emit turns that dialect's source into <c>#error</c> lines; the analyzer reports
/// the matching diagnostics. <see cref="InternalError"/> is the only problem the generator itself reports (GSH0048).
/// </remarks>
internal readonly struct TranspiledKernelResult(
    IMethodSymbol methodSymbol,
    string methodName,
    string className,
    string? @namespace,
    string cudaSource,
    string cppSource,
    string managedCSharpSource,
    EquatableArray<string> cudaIncludes,
    EquatableArray<string> cppIncludes,
    string internalError) : IEquatable<TranspiledKernelResult>
{
    public IMethodSymbol MethodSymbol { get; } = methodSymbol;
    public string MethodName { get; } = methodName;
    public string ClassName { get; } = className;
    public string? Namespace { get; } = @namespace;
    public string CudaSource { get; } = cudaSource;
    public string CppSource { get; } = cppSource;
    public string GetSource(bool isCuda) => isCuda ? CudaSource : CppSource;
    public string ManagedCSharpSource { get; } = managedCSharpSource;

    /// <summary>The <c>#include</c> targets of the CUDA source.</summary>
    public EquatableArray<string> CudaIncludes { get; } = cudaIncludes;

    /// <summary>The <c>#include</c> targets of the C++ source.</summary>
    public EquatableArray<string> CppIncludes { get; } = cppIncludes;

    public EquatableArray<string> GetIncludes(bool isCuda) => isCuda ? CudaIncludes : CppIncludes;

    /// <summary>The exception that stopped the transpilation (GSH0048), or empty.</summary>
    public string InternalError { get; } = internalError;

    public bool Equals(TranspiledKernelResult other)
    {
        return MethodName == other.MethodName &&
               ClassName == other.ClassName &&
               Namespace == other.Namespace &&
               CudaSource == other.CudaSource &&
               CppSource == other.CppSource &&
               ManagedCSharpSource == other.ManagedCSharpSource &&
               CudaIncludes.Equals(other.CudaIncludes) &&
               CppIncludes.Equals(other.CppIncludes) &&
               InternalError == other.InternalError;
    }

    public override bool Equals(object? obj) => obj is TranspiledKernelResult other && Equals(other);

    public override int GetHashCode() => HashHelper.Combine(MethodName, ClassName, Namespace, CudaSource, CppSource,
        ManagedCSharpSource, CudaIncludes, CppIncludes, InternalError);

    public static bool operator ==(TranspiledKernelResult left, TranspiledKernelResult right) => left.Equals(right);
    public static bool operator !=(TranspiledKernelResult left, TranspiledKernelResult right) => !left.Equals(right);
}
