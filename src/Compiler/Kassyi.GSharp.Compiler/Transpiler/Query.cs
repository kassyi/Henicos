namespace Kassyi.GSharp.Compiler.Transpiler;

public readonly struct Query(
    SemanticModel semanticModel,
    ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclIntrinsicMap,
    HashSet<INamedTypeSymbol> usedStructs)
{
    public SemanticModel SemanticModel { get; } = semanticModel;
    public ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> BclIntrinsicMap { get; } = bclIntrinsicMap;
    public HashSet<INamedTypeSymbol> UsedStructs { get; } = usedStructs;
}
