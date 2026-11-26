namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// The <c>GpuBigIntExpr</c> types of a compilation: the type of the GpuMath library and the two namespaces it had
/// before.
/// </summary>
internal readonly struct GpuBigIntExprTypes
{
    private readonly INamedTypeSymbol? _bigNum;
    private readonly INamedTypeSymbol? _expressions;
    private readonly INamedTypeSymbol? _gpuBigInt;

    private GpuBigIntExprTypes(INamedTypeSymbol? bigNum, INamedTypeSymbol? expressions, INamedTypeSymbol? gpuBigInt)
    {
        _bigNum = bigNum;
        _expressions = expressions;
        _gpuBigInt = gpuBigInt;
    }

    /// <summary>The types that <paramref name="compilation"/> declares or references; false when it has none.</summary>
    public static bool TryGet(Compilation compilation, out GpuBigIntExprTypes types)
    {
        types = new GpuBigIntExprTypes(
            compilation.GetTypeByMetadataName("Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions.GpuBigIntExpr"),
            compilation.GetTypeByMetadataName("Kassyi.GSharp.Stdlib.GpuMath.Expressions.GpuBigIntExpr"),
            compilation.GetTypeByMetadataName("Kassyi.GSharp.GpuBigInt.Expressions.GpuBigIntExpr"));
        return types._bigNum is not null || types._expressions is not null || types._gpuBigInt is not null;
    }

    /// <summary>Whether <paramref name="type"/> is one of the types, or a nullable of one.</summary>
    public bool Matches(ITypeSymbol? type) =>
        type?.UnwrapNullable() is INamedTypeSymbol { OriginalDefinition: var definition } &&
        (definition.IsEqualTo(_bigNum) || definition.IsEqualTo(_expressions) || definition.IsEqualTo(_gpuBigInt));
}
