namespace Kassyi.GSharp.Compiler.Transpiler;

using Rules;

public struct TranspileContext(
    SourceWriter writer,
    HashSet<INamedTypeSymbol> usedStructs,
    OverrideScopeStack localVariableOverrides,
    Stack<int> activeSwitchIds,
    SemanticModel semanticModel,
    Action<TranspileFailure>? reportFailure,
    RoslynToCudaVisitor visitor,
    ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclIntrinsicMap,
    ITranspilerDialect dialect)
{
    public Emitter Emitter = new(writer, localVariableOverrides, activeSwitchIds, dialect);
    public Query Query = new(semanticModel, bclIntrinsicMap, usedStructs);
    public Reporter Reporter = new(reportFailure, semanticModel);

    public RoslynToCudaVisitor Visitor { get; } = visitor;
    internal TranspilerRuleRegistry RuleRegistry => TranspilerRuleRegistry.Default;
}
