namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Metadata specifying a target CUDA intrinsic function name and optional argument casting.</summary>
public readonly record struct IntrinsicMappingInfo(string CudaIntrinsicName, string? CastFirstArgumentTo)
{
    public readonly string CudaIntrinsicName = CudaIntrinsicName;
    public readonly string? CastFirstArgumentTo = CastFirstArgumentTo;
}
