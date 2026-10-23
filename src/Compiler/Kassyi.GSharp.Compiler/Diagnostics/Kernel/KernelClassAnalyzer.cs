namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Checks the kernels of a class together: overloads whose launch wrappers cannot be told apart (GSH0059), and
/// parameter structs that map to the same <c>AotContexts</c> name (GSH0058).
/// </summary>
/// <remarks>
/// The generator leaves the reported overload and struct out of the generated code with the same helpers
/// (<see cref="KernelClassModel.TryFindIndistinguishableOverload"/>, <see cref="StructMarshalingEmitter.CollectUniqueStructs"/>),
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
        var kernels = ImmutableArray.CreateBuilder<IMethodSymbol>();
        type.GetMembers()
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .Where(static method => method.HasAttribute(KnownAttributes.GpuKernel))
            .AddTo(kernels);
        if (kernels.Count == 0)
        {
            return;
        }

        // The structs are not needed for the overload check
        var hosts = ImmutableArray.CreateBuilder<KernelHostModel>(kernels.Count);
        kernels
            .AsStackEnumerable()
            .Select(static kernel => KernelHostModel.Create(kernel, default))
            .AddTo(hosts);

        for (var i = 0; i < kernels.Count; i++)
        {
            if (KernelClassModel.TryFindIndistinguishableOverload(hosts, i, out var earlier, out var wrapper))
            {
                var kernel = kernels[i];
                context.ReportDiagnostic(
                    GSharpDiagnostics.IndistinguishableKernelOverloads,
                    kernel.Locations.Length > 0 ? kernel.Locations[0] : Location.None,
                    hosts[earlier].DisplaySignature,
                    hosts[i].DisplaySignature,
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
                kept.GetFullyQualifiedCsharpName(),
                duplicate.GetFullyQualifiedCsharpName(),
                name,
                type.Name);
        }
    }
}
