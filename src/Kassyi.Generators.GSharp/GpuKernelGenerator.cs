using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Kassyi.Generators.GSharp;

[Generator]
public class GpuKernelGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var kernelMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, "GpuKernel"),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx, "GpuKernel"))
            .Where(static m => m is not null);

        var deviceMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, "GpuDevice"),
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx, "GpuDevice"))
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
            var visitor = new Transpiler.RoslynToCudaVisitor(semanticModel, context.ReportDiagnostic);
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
            var visitor = new Transpiler.RoslynToCudaVisitor(semanticModel, context.ReportDiagnostic);
            var deviceCode = visitor.GenerateCudaSource(syntaxNode, isDevice: true);
            deviceSourceBuilder.AppendLine(deviceCode);
        }

        var commonDeviceSource = deviceSourceBuilder.ToString();

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
            var visitor = new Transpiler.RoslynToCudaVisitor(semanticModel, context.ReportDiagnostic);
            var cppSource = visitor.GenerateCudaSource(syntaxNode, isDevice: false);

            var finalSource = """
                // GSharp Prelude
                #include <cooperative_groups.h>
                namespace cg=cooperative_groups;

                __device__ __forceinline__ unsigned int __gsharp_add_cc(unsigned int a,unsigned int b,unsigned long long*carry){
                unsigned int res;
                unsigned int c_in=(unsigned int)*carry;
                unsigned int c_out;
                asm volatile(
                "add.cc.u32 %0, %2, -1;\n\t"
                "addc.cc.u32 %0, %3, %4;\n\t"
                "addc.u32 %1, 0, 0;"
                :"=&r"(res),"=r"(c_out)
                :"r"(c_in),"r"(a),"r"(b)
                );
                *carry=c_out;
                return res;
                }
                __device__ __forceinline__ unsigned int __gsharp_sub_cc(unsigned int a,unsigned int b,long long*borrow){
                unsigned int res;
                unsigned int dummy;
                unsigned int b_in=(unsigned int)*borrow;
                unsigned int b_out;
                asm volatile(
                "sub.cc.u32 %1, 0, %3;\n\t"
                "subc.cc.u32 %0, %4, %5;\n\t"
                "subc.u32 %2, 0, 0;"
                :"=&r"(res),"=&r"(dummy),"=r"(b_out)
                :"r"(b_in),"r"(a),"r"(b)
                );
                *borrow=b_out&1;
                return res;
                }

                """ + commonDeviceSource + cppSource;

            // Generate the C# Wrapper Class
            var wrapperSource = WrapperEmitter.GenerateWrapperClass(method, finalSource);

            context.AddSource($"{method.ContainingType.Name}_{method.Name}_Wrapper.g.cs",
                SourceText.From(wrapperSource, Encoding.UTF8));
        }
    }
}
