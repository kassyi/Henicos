namespace Kassyi.GSharp.Compiler.Diagnostics.Host;

/// <summary>
/// Reports <c>SharedMemory&lt;T&gt;</c> declarations that managed execution cannot tell apart (GSH0065).
/// </summary>
/// <remarks>
/// [WHY] A declaration is one buffer per block, looked up by a key of the declaration. Under managed execution a device
/// function that never reaches a barrier runs as the user's own C#, where the constructor knows its caller only by
/// <c>[CallerFilePath]</c>, <c>[CallerLineNumber]</c> and <c>[CallerMemberName]</c>. Two declarations with the same file,
/// line and method name share one buffer there, while they get two on the GPU.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SharedMemoryDeclarationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GSharpDiagnostics.SharedMemoryDeclarationsShareLine);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSemanticModelAction(AnalyzeSemanticModel);
    }

    private static void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
    {
        var semanticModel = context.SemanticModel;
        var seen = new HashSet<(int Line, string MemberName)>();
        foreach (var node in semanticModel.SyntaxTree.GetRoot(context.CancellationToken).DescendantNodes())
        {
            if (node is not BaseObjectCreationExpressionSyntax creation ||
                semanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol constructor ||
                !constructor.ContainingType.IsSharedMemoryType() ||
                !IsCallerKeyedConstructor(constructor))
            {
                continue;
            }

            var memberName = GetCallerMemberName(semanticModel.GetEnclosingSymbol(creation.SpanStart, context.CancellationToken));
            var line = creation.GetLocation().GetLineSpan().StartLinePosition.Line;
            if (!seen.Add((line, memberName)))
            {
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.SharedMemoryDeclarationsShareLine,
                    creation.GetLocation(), memberName));
            }
        }
    }

    // The constructor that takes the key itself is the one the compiler generates code with
    private static bool IsCallerKeyedConstructor(IMethodSymbol constructor) =>
        constructor.Parameters.Length < 2 || constructor.Parameters[1].Type.SpecialType != SpecialType.System_UInt64;

    // Mirrors what the C# compiler passes for [CallerMemberName]: the member that declares a lambda or local function
    private static string GetCallerMemberName(ISymbol? symbol)
    {
        while (symbol is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        {
            symbol = symbol.ContainingSymbol;
        }

        return symbol switch
        {
            IMethodSymbol { AssociatedSymbol: { } associated } => associated.Name,
            null => string.Empty,
            _ => symbol.Name
        };
    }
}
