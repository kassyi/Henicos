namespace Kassyi.GSharp.Compiler.Transpiler;

public struct TranspileContext
{
    public SourceWriter Writer;
    public HashSet<INamedTypeSymbol> UsedStructs;
    public Dictionary<string, RoslynToCudaVisitor.OverrideValue> LocalVariableOverrides;
    public bool HasEmittedDynamicSmemBase;

    public SemanticModel SemanticModel { get; }
    public Action<Diagnostic>? ReportDiagnostic { get; }

    // Visitor 自身への参照を保持し、ハンドラ側から子ノードの Visit を呼び出せるようにする
    public RoslynToCudaVisitor Visitor { get; }

    public TranspileContext(
        SourceWriter writer,
        HashSet<INamedTypeSymbol> usedStructs,
        Dictionary<string, RoslynToCudaVisitor.OverrideValue> localVariableOverrides,
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
