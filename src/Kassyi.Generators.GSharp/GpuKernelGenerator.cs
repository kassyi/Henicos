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
            deviceSourceBuilder.AppendLine();
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

            var finalSource = commonDeviceSource + cppSource;

            // Generate the C# Wrapper Class
            var wrapperSource = GenerateWrapperClass(method, finalSource);

            context.AddSource($"{method.ContainingType.Name}_{method.Name}_Wrapper.g.cs",
                SourceText.From(wrapperSource, Encoding.UTF8));
        }
    }

    // Removed GenerateCppSource as it's now handled by the visitor

    private static string GenerateWrapperClass(IMethodSymbol method, string cppSource)
    {
        var className = method.ContainingType.Name + "Wrapper";
        var namespaceName = method.ContainingNamespace.IsGlobalNamespace
            ? "Kassyi.GSharp.Generated"
            : method.ContainingNamespace.ToDisplayString();

        var writer = new SourceWriter();
        writer.AppendLine("""
            // <auto-generated/>
            using System;
            using System.Runtime.InteropServices;
            using global::Kassyi.GSharp.Core;
            """);

        writer.AppendLine();
        writer.AppendLine($"namespace {namespaceName}");
        using (writer.Scope())
        {
            writer.AppendLine($"public static partial class {className}");
            using (writer.Scope())
            {
                WriteKernelSource(writer, method, cppSource);
                WriteLaunchMethod(writer, method);
            }
        }

        return writer.ToString();
    }

    private static void WriteKernelSource(SourceWriter writer, IMethodSymbol method, string cppSource)
    {
        var escapedSource = cppSource.Replace("\"", "\"\"");
        writer.AppendLine($"""public const string {method.Name}_CudaSource = @"{escapedSource}";""");
        writer.AppendLine();
    }

    private static void WriteLaunchMethod(SourceWriter writer, IMethodSymbol method)
    {
        WriteLaunchMethodSignature(writer, method);
        using (writer.Scope())
        {
            WriteKernelCompilationCall(writer, method);
            WriteVariablesInitialization(writer, method);

            writer.AppendLine("try");
            using (writer.Scope())
            {
                WriteMarshalAndLaunchKernel(writer, method);
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                WriteCleanup(writer, method);
            }
        }
    }

    private static void WriteLaunchMethodSignature(SourceWriter writer, IMethodSymbol method)
    {
        var paramList = string.Join(", ", method.Parameters.Select(p => $"{p.Type.ToDisplayString()} {p.Name}"));
        if (!string.IsNullOrEmpty(paramList))
        {
            paramList += ", ";
        }

        paramList +=
            "uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1, uint blockDimZ = 1, uint sharedMemBytes = 0";

        writer.AppendLine($"public static void Launch_{method.Name}({paramList})");
    }

    private static bool IsJitConstant(IParameterSymbol p) => p.GetAttributes().Any(a =>
        a.AttributeClass?.Name == "JitConstantAttribute" || a.AttributeClass?.Name == "JitConstant");

    private static void WriteKernelCompilationCall(SourceWriter writer, IMethodSymbol method)
    {
        var jitConstants = method.Parameters.Where(IsJitConstant).ToList();
        if (jitConstants.Any())
        {
            var defines = string.Join(",\n", jitConstants.Select(jc => $"$\"#define {jc.Name} {{{jc.Name}}}\""));
            writer.AppendLine($$"""
                var __gsharp_dynamic_src = string.Join(System.Environment.NewLine, new string[] {
                    {{defines}},
                    {{method.Name}}_CudaSource
                });
                """);
            writer.AppendLine(
                $"""IntPtr hfunc = global::Kassyi.GSharp.Runtime.Transpiler.Pipeline.JitEngine.GetOrCompileKernel(__gsharp_dynamic_src, "{method.Name}");""");
        }
        else
        {
            writer.AppendLine(
                $"""IntPtr hfunc = global::Kassyi.GSharp.Runtime.Transpiler.Pipeline.JitEngine.GetOrCompileKernel({method.Name}_CudaSource, "{method.Name}");""");
        }

        writer.AppendLine();
    }

    private static void WriteVariablesInitialization(SourceWriter writer, IMethodSymbol method)
    {
        foreach (var p in method.Parameters.Where(p => !IsJitConstant(p)))
        {
            if (p.Type is IArrayTypeSymbol)
            {
                writer.AppendLine($"IntPtr d_{p.Name} = IntPtr.Zero;");
            }

            writer.AppendLine($"IntPtr pArg_{p.Name} = IntPtr.Zero;");
        }
    }

    private static void WriteMarshalAndLaunchKernel(SourceWriter writer, IMethodSymbol method)
    {
        var kernelParamsArray = new List<string>();

        foreach (var p in method.Parameters.Where(p => !IsJitConstant(p)))
        {
            WriteParameterMarshal(writer, p, kernelParamsArray);
        }

        writer.AppendLine();
        writer.AppendLine($"IntPtr[] kernelParams = [{string.Join(", ", kernelParamsArray)}];");
        writer.AppendLine();

        WriteLaunchKernelCall(writer);

        foreach (var p in method.Parameters.Where(p => !IsJitConstant(p)))
        {
            WriteParameterUnmarshal(writer, p);
        }
    }

    private static void WriteCleanup(SourceWriter writer, IMethodSymbol method)
    {
        foreach (var p in method.Parameters.Where(p => !IsJitConstant(p)))
        {
            WriteParameterFree(writer, p);
        }
    }

    private static void WriteParameterMarshal(SourceWriter writer, IParameterSymbol p,
        List<string> kernelParamsArray)
    {
        if (p.Type is IArrayTypeSymbol arrayType)
        {
            writer.AppendLine($"""
                // Marshal array: {p.Name}
                int {p.Name}_size = {p.Name}.Length * System.Runtime.InteropServices.Marshal.SizeOf(typeof({arrayType.ElementType.ToDisplayString()}));
                CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));

                System.Runtime.InteropServices.GCHandle handle_{p.Name} = System.Runtime.InteropServices.GCHandle.Alloc({p.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                """);
            using (writer.Scope())
            {
                writer.AppendLine($"""
                    IntPtr p_{p.Name} = handle_{p.Name}.AddrOfPinnedObject();
                    CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, p_{p.Name}, {p.Name}_size));
                    """);
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                writer.AppendLine($"handle_{p.Name}.Free();");
            }

            writer.AppendLine($"""
                pArg_{p.Name} = System.Runtime.InteropServices.Marshal.AllocHGlobal(IntPtr.Size);
                System.Runtime.InteropServices.Marshal.WriteIntPtr(pArg_{p.Name}, d_{p.Name});
                """);

            kernelParamsArray.Add($"pArg_{p.Name}");
        }
        else
        {
            // Scalar value type
            writer.AppendLine($"""
                // Marshal scalar: {p.Name}
                pArg_{p.Name} = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(typeof({p.Type.ToDisplayString()})));
                System.Runtime.InteropServices.Marshal.StructureToPtr({p.Name}, pArg_{p.Name}, false);
                """);

            kernelParamsArray.Add($"pArg_{p.Name}");
        }
    }

    private static void WriteLaunchKernelCall(SourceWriter writer)
    {
        writer.AppendLine("""
            CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
                hfunc,
                gridDimX, gridDimY, gridDimZ, // Grid dim
                blockDimX, blockDimY, blockDimZ, // Block dim
                sharedMemBytes, // Shared mem
                IntPtr.Zero, // Stream
                kernelParams,
                IntPtr.Zero // Extra
            ));
            """);
        writer.AppendLine();
    }

    private static void WriteParameterUnmarshal(SourceWriter writer, IParameterSymbol p)
    {
        if (p.Type is not IArrayTypeSymbol)
        {
            return;
        }

        writer.AppendLine($"""
            // Copy back array: {p.Name}
            System.Runtime.InteropServices.GCHandle handleBack_{p.Name} = System.Runtime.InteropServices.GCHandle.Alloc({p.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            """);
        using (writer.Scope())
        {
            writer.AppendLine($"""
                IntPtr p_{p.Name} = handleBack_{p.Name}.AddrOfPinnedObject();
                CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_{p.Name}, d_{p.Name}, {p.Name}_size));
                """);
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            writer.AppendLine($"handleBack_{p.Name}.Free();");
        }
    }

    private static void WriteParameterFree(SourceWriter writer, IParameterSymbol p)
    {
        if (p.Type is IArrayTypeSymbol)
        {
            writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_{p.Name});");
        }

        writer.AppendLine(
            $"if (pArg_{p.Name} != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(pArg_{p.Name});");
    }
}
