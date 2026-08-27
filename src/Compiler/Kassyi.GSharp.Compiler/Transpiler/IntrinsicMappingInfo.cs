namespace Kassyi.GSharp.Compiler.Transpiler;

public readonly record struct IntrinsicMappingInfo(string CudaIntrinsicName, string? CastFirstArgumentTo)
{
    public readonly string CudaIntrinsicName = CudaIntrinsicName;
    public readonly string? CastFirstArgumentTo = CastFirstArgumentTo;
}
