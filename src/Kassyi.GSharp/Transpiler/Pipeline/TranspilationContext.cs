namespace Kassyi.GSharp.Transpiler.Pipeline;

/// <summary>
/// Extracted metadata from a [GpuKernel] method.
/// </summary>
public record GpuKernelMetadata(
    MethodDefinition MethodDefinition,
    string AssemblyPath,
    string DeclaringTypeFullName,
    string MethodName
);

/// <summary>
/// The result of the CUDA generation phase.
/// </summary>
public record TranspiledSource(
    GpuKernelMetadata Metadata,
    string CudaCode,
    string SourceHash
);

/// <summary>
/// The result of the NVCC backend compilation phase.
/// </summary>
public record CompiledBinary(
    TranspiledSource Source,
    string DllPath
);