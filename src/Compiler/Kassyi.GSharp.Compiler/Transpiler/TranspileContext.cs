namespace Kassyi.GSharp.Compiler.Transpiler;

public struct TranspileContext(
    SourceWriter writer,
    HashSet<INamedTypeSymbol> usedStructs,
    Dictionary<string, OverrideValue> localVariableOverrides,
    SemanticModel semanticModel,
    Action<Diagnostic>? reportDiagnostic,
    RoslynToCudaVisitor visitor,
    ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclIntrinsicMap)
{
    public SourceWriter Writer = writer;
    public HashSet<INamedTypeSymbol> UsedStructs = usedStructs;
    public Dictionary<string, OverrideValue> LocalVariableOverrides = localVariableOverrides;
    public bool HasEmittedDynamicSmemBase = false;

    public SemanticModel SemanticModel { get; } = semanticModel;
    public Action<Diagnostic>? ReportDiagnostic { get; } = reportDiagnostic;
    public ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> BclIntrinsicMap { get; } = bclIntrinsicMap;

    public RoslynToCudaVisitor Visitor { get; } = visitor;
}

public readonly struct OverrideValue
{
    public readonly string? StringValue;
    public readonly SyntaxNode? NodeValue;
　
    public OverrideValue(string stringValue)
    {
        StringValue = stringValue;
        NodeValue = null;
    }

    public OverrideValue(SyntaxNode nodeValue)
    {
        NodeValue = nodeValue;
        StringValue = null;
    }

    public void WriteTo(RoslynToCudaVisitor visitor)
    {
        if (NodeValue != null)
        {
            visitor.Visit(NodeValue);
        }
        else if (StringValue != null)
        {
            visitor._context.Writer.Append(StringValue);
        }
    }
}
