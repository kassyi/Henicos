namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Checks the kernels of a class together: overloads whose launch wrappers cannot be told apart (GSH0059), and
/// parameter structs that map to the same <c>AotContexts</c> name (GSH0058).
/// </summary>
/// <remarks>
/// The generator leaves the reported overload and struct out of the generated code with the same helpers
/// (<see cref="KernelHostNames.TryFindIndistinguishableOverload"/>, <see cref="StructMarshalingEmitter.CollectUniqueStructs"/>),
/// so the generated code stays valid while this analyzer reports why.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class KernelClassAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.IndistinguishableKernelOverloads,
        GSharpDiagnostics.DuplicateAotContextName);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        var kernels = ImmutableArray.CreateBuilder<(IMethodSymbol Method, string Source, string ManagedCSharpSource)>();
        type.GetMembers()
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .Where(static method => method.HasAttribute(KnownAttributes.GpuKernel))
            .Select(static method => (method, "", ""))
            .AddTo(kernels);
        if (kernels.Count == 0)
        {
            return;
        }

        foreach (var (kernel, _, _) in kernels)
        {
            if (KernelHostNames.TryFindIndistinguishableOverload(kernel, out var earlier, out var wrapper))
            {
                context.ReportDiagnostic(
                    GSharpDiagnostics.IndistinguishableKernelOverloads,
                    kernel.Locations.Length > 0 ? kernel.Locations[0] : Location.None,
                    KernelHostNames.FormatSignature(earlier),
                    KernelHostNames.FormatSignature(kernel),
                    wrapper);
            }
        }

        // [WHY] AotContexts drops a trailing "Context", namespaces and type arguments, so different structs can share one name
        var duplicates = ImmutableArray.CreateBuilder<(INamedTypeSymbol Kept, INamedTypeSymbol Duplicate, string Name)>();
        StructMarshalingEmitter.CollectUniqueStructs(kernels, duplicates);
        foreach (var (kept, duplicate, name) in duplicates)
        {
            context.ReportDiagnostic(
                GSharpDiagnostics.DuplicateAotContextName,
                duplicate.Locations.Length > 0 ? duplicate.Locations[0] : Location.None,
                FormatStructName(kept),
                FormatStructName(duplicate),
                name,
                type.Name);
        }
    }

    private static string FormatStructName(INamedTypeSymbol type)
    {
        using var writer = new SourceWriter();
        writer.AppendFullyQualifiedCsharpName(type);
        return writer.ToString();
    }
}
