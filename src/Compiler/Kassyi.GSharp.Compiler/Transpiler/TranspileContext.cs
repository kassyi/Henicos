namespace Kassyi.GSharp.Compiler.Transpiler;

public struct TranspileContext
{
    public SourceWriter Writer;
    public HashSet<INamedTypeSymbol> UsedStructs;
    public Dictionary<string, OverrideValue> LocalVariableOverrides;
    public bool HasEmittedDynamicSmemBase;

    public SemanticModel SemanticModel { get; }
    public Action<Diagnostic>? ReportDiagnostic { get; }

    // Visitor 自身への参照を保持し、ハンドラ側から子ノードの Visit を呼び出せるようにする
    public RoslynToCudaVisitor Visitor { get; }

    public TranspileContext(
        SourceWriter writer,
        HashSet<INamedTypeSymbol> usedStructs,
        Dictionary<string, OverrideValue> localVariableOverrides,
        SemanticModel semanticModel,
        Action<Diagnostic>? reportDiagnostic,
        RoslynToCudaVisitor visitor)
    {
        Writer = writer;
        UsedStructs = usedStructs;
        LocalVariableOverrides = localVariableOverrides;
        SemanticModel = semanticModel;
        ReportDiagnostic = reportDiagnostic;
        Visitor = visitor;
        HasEmittedDynamicSmemBase = false;
    }
}

public readonly struct OverrideValue
{
    public readonly string? StringValue;
    public readonly Microsoft.CodeAnalysis.SyntaxNode? NodeValue;

    public OverrideValue(string stringValue)
    {
        StringValue = stringValue;
        NodeValue = null;
    }

    public OverrideValue(Microsoft.CodeAnalysis.SyntaxNode nodeValue)
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
