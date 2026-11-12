using Microsoft.CodeAnalysis.Operations;

namespace Kassyi.GSharp.Compiler.Diagnostics.Kernel;

/// <summary>
/// Reports a <c>[GpuKernel]</c> on a declaration that cannot have a launcher (GSH0111), and a kernel parameter whose
/// type has no device representation (GSH0112).
/// </summary>
/// <remarks>
/// The generator leaves the same kernels out with <see cref="KernelDeclaration.IsLaunchable"/>, so the generated C#
/// stays valid while this analyzer reports why. Lambdas and local functions are not declared symbols, so they are
/// found through their operations.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class KernelDeclarationAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        GSharpDiagnostics.KernelDeclarationNotSupported,
        GSharpDiagnostics.KernelParameterHasNoDeviceRepresentation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterOperationAction(AnalyzeFunction, OperationKind.AnonymousFunction, OperationKind.LocalFunction);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context) =>
        Analyze((IMethodSymbol)context.Symbol, context.Compilation, context.ReportDiagnostic,
            context.CancellationToken);

    private static void AnalyzeFunction(OperationAnalysisContext context)
    {
        var function = context.Operation switch
        {
            IAnonymousFunctionOperation lambda => lambda.Symbol,
            ILocalFunctionOperation local => local.Symbol,
            _ => null
        };
        if (function is not null)
        {
            Analyze(function, context.Compilation, context.ReportDiagnostic, context.CancellationToken);
        }
    }

    private static void Analyze(IMethodSymbol method, Compilation compilation, Action<Diagnostic> report,
        CancellationToken cancellationToken)
    {
        // A finalizer is reported by GpuFinalizerRule (GSH0003); the implementing part of a partial method shares its
        // attributes with the defining part, which is analyzed
        if (method.MethodKind == MethodKind.Destructor || method.PartialDefinitionPart is not null ||
            method.GetAttribute(KnownAttributes.GpuKernel) is not { } attribute)
        {
            return;
        }

        var placement = KernelDeclaration.GetPlacement(method, compilation, cancellationToken);
        if (placement != KernelPlacement.Allowed)
        {
            report.ReportDiagnostic(
                GSharpDiagnostics.KernelDeclarationNotSupported,
                attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation() ??
                (method.Locations.Length > 0 ? method.Locations[0] : Location.None),
                KernelDeclaration.Describe(placement, method));
            return;
        }

        foreach (var parameter in method.Parameters)
        {
            if (KernelDeclaration.FindManagedReference(parameter.Type) is { } reference &&
                GpuCodeConstraints.GetRuntimeOnlyTypeName(reference) is null &&
                parameter.TryGetDeclaringSyntax<ParameterSyntax>(out var syntax))
            {
                report.ReportDiagnostic(
                    GSharpDiagnostics.KernelParameterHasNoDeviceRepresentation,
                    syntax,
                    parameter.Name,
                    $"{method.ContainingType.Name}.{method.Name}",
                    IteratorTypeResolver.FormatType(parameter.Type));
            }
        }
    }
}
