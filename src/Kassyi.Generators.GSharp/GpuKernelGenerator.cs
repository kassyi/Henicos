using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using Kassyi.Generators.GSharp.Transpiler;

namespace Kassyi.Generators.GSharp;

[Generator]
public class GpuKernelGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var kernelMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuKernel),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx, KnownAttributes.GpuKernel))
            .Where(static m => m is not null);

        var deviceMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuDevice),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx, KnownAttributes.GpuDevice))
            .Where(static m => m is not null);

        var kernelsAndDevices = kernelMethods.Collect().Combine(deviceMethods.Collect());
        var compilationAndAll = context.CompilationProvider.Combine(kernelsAndDevices);

        context.RegisterSourceOutput(compilationAndAll,
            static (spc, source) => Execute(source.Left, source.Right.Left, source.Right.Right, spc));
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node, string attributeName)
    {
        if (node is MethodDeclarationSyntax { AttributeLists.Count: > 0 } m)
        {
            return m.AttributeLists.Any(al => al.Attributes.Any(a => a.Name.ToString().Contains(attributeName)));
        }

        return false;
    }

    private static IMethodSymbol? GetSemanticTargetForGeneration(GeneratorSyntaxContext context, string attributeName)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;

        if (methodSymbol == null)
        {
            return null;
        }

        foreach (var attribute in methodSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == $"{attributeName}Attribute" ||
                attribute.AttributeClass?.Name == attributeName)
            {
                return methodSymbol;
            }
        }

        return null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<IMethodSymbol?> kernelMethods,
        ImmutableArray<IMethodSymbol?> deviceMethods, SourceProductionContext context)
    {
        if (kernelMethods.IsDefaultOrEmpty)
        {
            return;
        }

        var commonDeviceSource = GenerateCommonDeviceSource(compilation, deviceMethods, context);
        var preludeSource = GetPreludeSource();

        foreach (var method in kernelMethods.OfType<IMethodSymbol>()
                     .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                continue;
            }

            var syntaxNode = syntaxRef.GetSyntax() as MethodDeclarationSyntax;
            if (syntaxNode == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
            var visitor = new RoslynToCudaVisitor(semanticModel, context.ReportDiagnostic);
            var cppSource = visitor.GenerateCudaSource(syntaxNode, isDevice: false);

            var finalSource = preludeSource + commonDeviceSource + cppSource;

            // Generate the C# Wrapper Class
            var wrapperSource = WrapperEmitter.GenerateWrapperClass(method, finalSource);

            context.AddSource($"{method.ContainingType.Name}_{method.Name}_Wrapper.g.cs",
                SourceText.From(wrapperSource, Encoding.UTF8));
        }
    }

    private static string GenerateCommonDeviceSource(Compilation compilation,
        ImmutableArray<IMethodSymbol?> deviceMethods, SourceProductionContext context)
    {
        var deviceSourceBuilder = new StringBuilder();

        // Pass 1: Forward declarations
        foreach (var method in deviceMethods.OfType<IMethodSymbol>()
                     .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                continue;
            }

            var syntaxNode = syntaxRef.GetSyntax() as MethodDeclarationSyntax;
            if (syntaxNode == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
            var visitor = new RoslynToCudaVisitor(semanticModel, context.ReportDiagnostic);
            var signatureCode = visitor.GenerateCudaSignature(syntaxNode, isDevice: true);
            deviceSourceBuilder.AppendLine(signatureCode);
        }

        // Pass 2: Method bodies
        foreach (var method in deviceMethods.OfType<IMethodSymbol>()
                     .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
        {
            var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
            {
                continue;
            }

            var syntaxNode = syntaxRef.GetSyntax() as MethodDeclarationSyntax;
            if (syntaxNode == null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(syntaxRef.SyntaxTree);
            var visitor = new RoslynToCudaVisitor(semanticModel, context.ReportDiagnostic);
            var deviceCode = visitor.GenerateCudaSource(syntaxNode, isDevice: true);
            deviceSourceBuilder.AppendLine(deviceCode);
        }

        return deviceSourceBuilder.ToString();
    }

    private static string GetPreludeSource()
    {
        var assembly = typeof(GpuKernelGenerator).Assembly;
        var resourceName = "Kassyi.Generators.GSharp.Resources.Prelude.cu";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return "// Prelude not found\n";
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd() + "\n";
    }
}
