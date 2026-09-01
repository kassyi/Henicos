namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>Encapsulates output writers, symbol caches, semantic models, and diagnostics during transpilation.</summary>
public struct TranspileContext(
    SourceWriter writer,
    HashSet<INamedTypeSymbol> usedStructs,
    Dictionary<string, OverrideValue> localVariableOverrides,
    SemanticModel semanticModel,
    Action<Diagnostic>? reportDiagnostic,
    RoslynToCudaVisitor visitor,
    ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> bclIntrinsicMap,
    Dialects.ITranspilerDialect dialect)
{
    public SourceWriter Writer = writer;
    public HashSet<INamedTypeSymbol> UsedStructs = usedStructs;
    public Dictionary<string, OverrideValue> LocalVariableOverrides = localVariableOverrides;
    public bool HasEmittedDynamicSmemBase = false;
    public bool SkipLocalsInit = false;

    public SemanticModel SemanticModel { get; } = semanticModel;
    public Action<Diagnostic>? ReportDiagnostic { get; } = reportDiagnostic;
    public ImmutableDictionary<IMethodSymbol, IntrinsicMappingInfo> BclIntrinsicMap { get; } = bclIntrinsicMap;
    public Dialects.ITranspilerDialect Dialect { get; } = dialect;

    public RoslynToCudaVisitor Visitor { get; } = visitor;

    public void AppendType(ITypeSymbol? typeSymbol) => Dialect.AppendType(ref this, typeSymbol);
    public (bool IsRefPointer, bool IsPointerType, bool HasRestrict) AppendParameterCppType(ParameterSyntax p, ITypeSymbol? typeSymbol) => Dialect.AppendParameter(ref this, p, typeSymbol);
}

/// <summary>Represents an overridden variable substitution value (literal string or AST node) for macro expansions.</summary>
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
            visitor.Context.Writer.Append(StringValue);
        }
    }
}
