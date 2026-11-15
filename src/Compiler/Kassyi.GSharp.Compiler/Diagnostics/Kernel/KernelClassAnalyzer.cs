namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Checks the kernels of a class together: kernels whose launchers or warmup methods have the name and parameter types
/// of those of another kernel (GSH0059), and parameter and return structs that map to the same <c>AotContexts</c> name
/// (GSH0058).
/// </summary>
/// <remarks>
/// The generator leaves the reported kernel and struct out of the generated code with the same helpers
/// (<see cref="LauncherSignatures.FindConflicts"/>, <see cref="StructMarshalingEmitter.CollectUniqueStructs"/>),
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
        // The kernels that the generator emits; KernelDeclarationAnalyzer reports the others
        type.GetMembers()
            .AsStackEnumerable()
            .OfType<IMethodSymbol>()
            .Where(static method => method.HasAttribute(KnownAttributes.GpuKernel))
            .Where(context, static (method, analysis) =>
                KernelDeclaration.IsLaunchable(method, analysis.Compilation, analysis.CancellationToken))
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

        // GSharpEmitSource adds a {key}_Source constant per kernel, as the generator reads it
        var emitsSource =
            context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(
                "build_property.GSharpEmitSource", out var emitSource) &&
            string.Equals(emitSource, "true", StringComparison.OrdinalIgnoreCase);
        var conflicts = LauncherSignatures.FindConflicts(hosts, emitsSource);
        for (var i = 0; i < kernels.Count; i++)
        {
            if (conflicts[i] is { IsConflict: true } conflict)
            {
                var kernel = kernels[i];
                context.ReportDiagnostic(
                    GSharpDiagnostics.IndistinguishableKernelOverloads,
                    kernel.Locations.Length > 0 ? kernel.Locations[0] : Location.None,
                    hosts[i].DisplaySignature,
                    conflict.Member,
                    conflict.IsClassMember
                        ? "G# generates in every class with kernels"
                        : $"the [GpuKernel] method '{hosts[conflict.Earlier].DisplaySignature}' also declares or " +
                          "makes G# generate");
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
