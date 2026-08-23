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
            var wrapperSource = GenerateWrapperClass(method, finalSource);

            context.AddSource($"{method.ContainingType.Name}_{method.Name}_Wrapper.g.cs",
                SourceText.From(wrapperSource, Encoding.UTF8));
        }
    }

    // Removed GenerateCppSource as it's now handled by the visitor


    private static bool IsCustomStruct(ITypeSymbol type)
    {
        return type is INamedTypeSymbol named &&
               named.TypeKind == TypeKind.Struct &&
               named.ContainingNamespace.Name != "System";
    }

    private static void CollectStructs(ITypeSymbol type, HashSet<INamedTypeSymbol> structs)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Struct } named ||
            named.ContainingNamespace.Name == TypeConstants.SystemNamespace)
        {
            return;
        }

        if (structs.Add(named))
        {
            foreach (var member in named.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
            {
                CollectStructs(member.Type, structs);
            }
        }
    }

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
                WriteStructMarshaling(writer, method);
                WriteLaunchMethod(writer, method, false);
                WriteLaunchMethod(writer, method, true);
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

    private static void WriteLaunchMethod(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        WriteLaunchMethodSignature(writer, method, isDevicePtr);
        using (writer.Scope())
        {
            writer.Append(
                "using var _cudaCtxScope = new global::Kassyi.GSharp.Runtime.Transpiler.Pipeline.JitEngine.CudaContextScope");
            using (writer.Parentheses())
            {
            }

            writer.AppendLine(";");
            WriteKernelCompilationCall(writer, method);
            WriteVariablesInitialization(writer, method, isDevicePtr);

            writer.AppendLine("try");
            using (writer.Scope())
            {
                WriteMarshalAndLaunchKernel(writer, method, isDevicePtr);
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                WriteCleanup(writer, method, isDevicePtr);
            }
        }
    }

    private static void WriteLaunchMethodSignature(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        var paramList = string.Join(", ", method.Parameters.Select(p =>
        {
            if (isDevicePtr && p.Type is IArrayTypeSymbol)
            {
                return $"IntPtr {p.Name}";
            }

            return $"{p.Type.ToDisplayString()} {p.Name}";
        }));
        if (!string.IsNullOrEmpty(paramList))
        {
            paramList += ", ";
        }

        paramList +=
            "uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1, uint blockDimZ = 1, uint sharedMemBytes = 0";

        var methodName = isDevicePtr ? $"Launch_{method.Name}_Device" : $"Launch_{method.Name}";
        writer.Append($"public static void {methodName}");
        using (writer.Parentheses())
        {
            writer.Append(paramList);
        }

        writer.AppendLine();
    }

    private static bool IsJitConstant(IParameterSymbol p) => p.GetAttributes().Any(a =>
        a.AttributeClass?.Name == "JitConstantAttribute" || a.AttributeClass?.Name == "JitConstant");

    private static void WriteKernelCompilationCall(SourceWriter writer, IMethodSymbol method)
    {
        var jitConstants = method.Parameters.Where(IsJitConstant).ToList();
        if (jitConstants.Any())
        {
            writer.AppendLine("var __gsharp_dynamic_src = string.Join");
            using (writer.Parentheses())
            {
                writer.Append("System.Environment.NewLine, new string");
                using (writer.Brackets())
                {
                }

                writer.AppendLine();
                using (writer.Scope())
                {
                    foreach (var jc in jitConstants)
                    {
                        writer.Append($"\"#define {jc.Name} \" + ");
                        using (writer.Parentheses())
                        {
                            writer.Append(jc.Name);
                        }

                        writer.Append(".ToString()");
                        writer.AppendLine(",");
                    }

                    writer.AppendLine($"{method.Name}_CudaSource");
                }
            }

            writer.AppendLine(";");

            writer.Append(
                "IntPtr hfunc = global::Kassyi.GSharp.Runtime.Transpiler.Pipeline.JitEngine.GetOrCompileKernel");
            using (writer.Parentheses())
            {
                writer.Append($"__gsharp_dynamic_src, \"{method.Name}\"");
            }

            writer.AppendLine(";");
        }
        else
        {
            writer.Append(
                "IntPtr hfunc = global::Kassyi.GSharp.Runtime.Transpiler.Pipeline.JitEngine.GetOrCompileKernel");
            using (writer.Parentheses())
            {
                writer.Append($"{method.Name}_CudaSource, \"{method.Name}\"");
            }

            writer.AppendLine(";");
        }

        writer.AppendLine();
    }


    private static void WriteStructMarshaling(SourceWriter writer, IMethodSymbol method)
    {
        var structTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var parameter in method.Parameters)
        {
            CollectStructs(parameter.Type, structTypes);
        }

        foreach (var s in structTypes)
        {
            var sName = s.ToDisplayString().Replace("global::", "").Replace(".", "_");
            var nativeName = $"__Native_{sName}";

            writer.AppendLine(
                "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]");
            writer.AppendLine($"public struct {nativeName}");
            using (writer.Scope())
            {
                foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
                {
                    if (member.Type is IArrayTypeSymbol)
                    {
                        writer.AppendLine($"public IntPtr {member.Name};");
                    }
                    else if (IsCustomStruct(member.Type))
                    {
                        var memberTypeName = member.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
                        writer.AppendLine($"public __Native_{memberTypeName} {member.Name};");
                    }
                    else
                    {
                        writer.AppendLine($"public {member.Type.ToDisplayString()} {member.Name};");
                    }
                }
            }

            writer.AppendLine();

            writer.AppendLine(
                $"public static {nativeName} Marshal_{sName}(ref {s.ToDisplayString()} host, bool isDevicePtr, System.Collections.Generic.List<IntPtr> allocatedPtrs)");
            using (writer.Scope())
            {
                writer.AppendLine($"var native = new {nativeName}();");
                foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
                {
                    if (member.Type is IArrayTypeSymbol arrayType)
                    {
                        writer.AppendLine($"if (host.{member.Name} != null)");
                        using (writer.Scope())
                        {
                            writer.AppendLine("if (isDevicePtr)");
                            using (writer.Scope())
                            {
                                writer.AppendLine(
                                    "throw new System.NotSupportedException(\"Passing a struct with arrays is not supported when isDevicePtr is true.\");");
                            }

                            writer.AppendLine("else");
                            using (writer.Scope())
                            {
                                writer.AppendLine(
                                    $"int size = host.{member.Name}.Length * System.Runtime.InteropServices.Marshal.SizeOf(typeof({arrayType.ElementType.ToDisplayString()}));");
                                writer.AppendLine(
                                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out native.{member.Name}, size));");
                                writer.AppendLine($"allocatedPtrs.Add(native.{member.Name});");
                                writer.AppendLine(
                                    $"var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.{member.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                                writer.AppendLine("try");
                                using (writer.Scope())
                                {
                                    writer.AppendLine(
                                        $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(native.{member.Name}, handle.AddrOfPinnedObject(), size));");
                                }

                                writer.AppendLine("finally { handle.Free(); }");
                            }
                        }
                    }
                    else if (IsCustomStruct(member.Type))
                    {
                        var memberTypeName = member.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
                        writer.AppendLine(
                            $"native.{member.Name} = Marshal_{memberTypeName}(ref host.{member.Name}, isDevicePtr, allocatedPtrs);");
                    }
                    else
                    {
                        writer.AppendLine($"native.{member.Name} = host.{member.Name};");
                    }
                }

                writer.AppendLine("return native;");
            }

            writer.AppendLine();

            writer.AppendLine(
                $"public static void Unmarshal_{sName}(ref {s.ToDisplayString()} host, ref {nativeName} native, bool isDevicePtr)");
            using (writer.Scope())
            {
                foreach (var member in s.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic))
                {
                    if (member.Type is IArrayTypeSymbol arrayType)
                    {
                        writer.AppendLine(
                            $"if (host.{member.Name} != null && !isDevicePtr && native.{member.Name} != IntPtr.Zero)");
                        using (writer.Scope())
                        {
                            writer.AppendLine(
                                $"int size = host.{member.Name}.Length * System.Runtime.InteropServices.Marshal.SizeOf(typeof({arrayType.ElementType.ToDisplayString()}));");
                            writer.AppendLine(
                                $"var handle = System.Runtime.InteropServices.GCHandle.Alloc(host.{member.Name}, System.Runtime.InteropServices.GCHandleType.Pinned);");
                            writer.AppendLine("try");
                            using (writer.Scope())
                            {
                                writer.AppendLine(
                                    $"CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(handle.AddrOfPinnedObject(), native.{member.Name}, size));");
                            }

                            writer.AppendLine("finally { handle.Free(); }");
                        }
                    }
                    else if (IsCustomStruct(member.Type))
                    {
                        var memberTypeName = member.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
                        writer.AppendLine(
                            $"Unmarshal_{memberTypeName}(ref host.{member.Name}, ref native.{member.Name}, isDevicePtr);");
                    }
                    else
                    {
                        writer.AppendLine($"host.{member.Name} = native.{member.Name};");
                    }
                }
            }

            writer.AppendLine();
        }
    }

    private static void WriteVariablesInitialization(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters.Where(p => !IsJitConstant(p)))
        {
            if (IsCustomStruct(p.Type))
            {
                writer.Append(
                    $"System.Collections.Generic.List<IntPtr> {p.Name}_allocatedPtrs = new System.Collections.Generic.List<IntPtr>");
                using (writer.Parentheses())
                {
                }

                writer.AppendLine(";");
                if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
                {
                    writer.AppendLine($"IntPtr d_{p.Name} = IntPtr.Zero;");
                }
            }
            else if (!isDevicePtr &&
                     (p.Type is IArrayTypeSymbol || p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out))
            {
                writer.AppendLine($"IntPtr d_{p.Name} = IntPtr.Zero;");
            }

            writer.AppendLine($"IntPtr pArg_{p.Name} = IntPtr.Zero;");
        }
    }

    private static void WriteMarshalAndLaunchKernel(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        var kernelParamsArray = new List<string>();

        foreach (var p in method.Parameters.Where(p => !IsJitConstant(p)))
        {
            WriteParameterMarshal(writer, p, kernelParamsArray, isDevicePtr);
        }

        writer.AppendLine();
        writer.Append("IntPtr");
        using (writer.Brackets())
        {
        }

        writer.Append(" kernelParams = ");
        using (writer.Brackets())
        {
            writer.Append(string.Join(", ", kernelParamsArray));
        }

        writer.AppendLine(";");
        writer.AppendLine();

        WriteLaunchKernelCall(writer, method);

        foreach (var p in method.Parameters.Where(p => !IsJitConstant(p)))
        {
            WriteParameterUnmarshal(writer, p, isDevicePtr);
        }
    }

    private static void WriteCleanup(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        foreach (var p in method.Parameters.Where(p => !IsJitConstant(p)))
        {
            WriteParameterFree(writer, p, isDevicePtr);
        }
    }

    private static void WriteParameterMarshal(SourceWriter writer, IParameterSymbol p,
        List<string> kernelParamsArray, bool isDevicePtr)
    {
        if (IsCustomStruct(p.Type))
        {
            var pTypeName = p.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
            writer.AppendLine(
                $"var native_{p.Name} = Marshal_{pTypeName}(ref {p.Name}, {(isDevicePtr ? "true" : "false")}, {p.Name}_allocatedPtrs);");

            if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
            {
                writer.AppendLine($"""
                    // Marshal ref/out struct: {p.Name}
                    int {p.Name}_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(__Native_{pTypeName}));
                    CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));
                    """);
                if (p.RefKind == RefKind.Ref)
                {
                    writer.AppendLine($"""
                        IntPtr p_{p.Name}_temp = System.Runtime.InteropServices.Marshal.AllocHGlobal({p.Name}_size);
                        try
                        """);
                    using (writer.Scope())
                    {
                        writer.AppendLine($"""
                            System.Runtime.InteropServices.Marshal.StructureToPtr(native_{p.Name}, p_{p.Name}_temp, false);
                            CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, p_{p.Name}_temp, {p.Name}_size));
                            """);
                    }

                    writer.AppendLine("finally");
                    using (writer.Scope())
                    {
                        writer.AppendLine($"System.Runtime.InteropServices.Marshal.FreeHGlobal(p_{p.Name}_temp);");
                    }
                }

                writer.AppendLine($"""
                    pArg_{p.Name} = System.Runtime.InteropServices.Marshal.AllocHGlobal(IntPtr.Size);
                    System.Runtime.InteropServices.Marshal.WriteIntPtr(pArg_{p.Name}, d_{p.Name});
                    """);
            }
            else
            {
                writer.AppendLine($"""
                    // Marshal scalar struct: {p.Name}
                    pArg_{p.Name} = System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(typeof(__Native_{p.Type.Name})));
                    System.Runtime.InteropServices.Marshal.StructureToPtr(native_{p.Name}, pArg_{p.Name}, false);
                    """);
            }

            kernelParamsArray.Add($"pArg_{p.Name}");
        }
        else if (p.Type is IArrayTypeSymbol arrayType)
        {
            if (isDevicePtr)
            {
                writer.AppendLine($"""
                    // Marshal device array: {p.Name}
                    pArg_{p.Name} = System.Runtime.InteropServices.Marshal.AllocHGlobal(IntPtr.Size);
                    System.Runtime.InteropServices.Marshal.WriteIntPtr(pArg_{p.Name}, {p.Name});
                    """);
            }
            else
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
            }

            kernelParamsArray.Add($"pArg_{p.Name}");
        }
        else if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
        {
            var typeString = p.Type.ToDisplayString();
            writer.AppendLine($"""
                // Marshal ref/out scalar: {p.Name}
                int {p.Name}_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof({typeString}));
                CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_{p.Name}, {p.Name}_size));
                """);

            if (p.RefKind == RefKind.Ref)
            {
                writer.AppendLine($"""
                    IntPtr p_{p.Name}_temp = System.Runtime.InteropServices.Marshal.AllocHGlobal({p.Name}_size);
                    try
                    """);
                using (writer.Scope())
                {
                    writer.AppendLine($"""
                        System.Runtime.InteropServices.Marshal.StructureToPtr({p.Name}, p_{p.Name}_temp, false);
                        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_{p.Name}, p_{p.Name}_temp, {p.Name}_size));
                        """);
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.AppendLine($"System.Runtime.InteropServices.Marshal.FreeHGlobal(p_{p.Name}_temp);");
                }
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

    private static void WriteLaunchKernelCall(SourceWriter writer, IMethodSymbol method)
    {
        var isCooperative = false;
        foreach (var attr in method.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "GpuKernelAttribute" && attr.AttributeClass?.Name != "GpuKernel")
            {
                continue;
            }

            var namedArg = attr.NamedArguments.FirstOrDefault(na => na.Key == "Cooperative");
            if (namedArg.Value.Value is true)
            {
                isCooperative = true;
            }

            break;
        }

        writer.Append("CudaException.ThrowIfError");
        using (writer.Parentheses())
        {
            writer.Append(isCooperative ? "CudaDriverApi.cuLaunchCooperativeKernel" : "CudaDriverApi.cuLaunchKernel");
            using (writer.Parentheses())
            {
                writer.AppendLine();
                writer.AppendLine("hfunc,");
                writer.AppendLine("gridDimX, gridDimY, gridDimZ, // Grid dim");
                writer.AppendLine("blockDimX, blockDimY, blockDimZ, // Block dim");
                writer.AppendLine("sharedMemBytes, // Shared mem");
                if (isCooperative)
                {
                    writer.AppendLine("IntPtr.Zero, // Stream");
                    writer.Append("kernelParams");
                }
                else
                {
                    writer.AppendLine("IntPtr.Zero, // Stream");
                    writer.AppendLine("kernelParams,");
                    writer.Append("IntPtr.Zero // Extra");
                }

                writer.AppendLine();
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void WriteParameterUnmarshal(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (IsCustomStruct(p.Type))
        {
            var pTypeName = p.Type.ToDisplayString().Replace("global::", "").Replace(".", "_");
            if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
            {
                writer.AppendLine($"""
                    // Unmarshal ref/out struct: {p.Name}
                    IntPtr p_{p.Name}_temp_out = System.Runtime.InteropServices.Marshal.AllocHGlobal({p.Name}_size);
                    try
                    """);
                using (writer.Scope())
                {
                    writer.AppendLine($"""
                        CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_{p.Name}_temp_out, d_{p.Name}, {p.Name}_size));
                        native_{p.Name} = (__Native_{pTypeName})System.Runtime.InteropServices.Marshal.PtrToStructure(p_{p.Name}_temp_out, typeof(__Native_{pTypeName}));
                        """);
                }
                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    writer.AppendLine($"System.Runtime.InteropServices.Marshal.FreeHGlobal(p_{p.Name}_temp_out);");
                }
            }
            writer.AppendLine($"Unmarshal_{pTypeName}(ref {p.Name}, ref native_{p.Name}, {(isDevicePtr ? "true" : "false")});");
        }
        else if (p.Type is IArrayTypeSymbol arrayType)
        {
            if (!isDevicePtr)
            {
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
        }
        else if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
        {
            var typeString = p.Type.ToDisplayString();
            writer.AppendLine($"""
                // Unmarshal ref/out scalar: {p.Name}
                IntPtr p_{p.Name}_temp = System.Runtime.InteropServices.Marshal.AllocHGlobal({p.Name}_size);
                try
                """);
            using (writer.Scope())
            {
                writer.AppendLine($"""
                    CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_{p.Name}_temp, d_{p.Name}, {p.Name}_size));
                    {p.Name} = ({typeString})System.Runtime.InteropServices.Marshal.PtrToStructure(p_{p.Name}_temp, typeof({typeString}));
                    """);
            }

            writer.AppendLine("finally");
            using (writer.Scope())
            {
                writer.AppendLine($"System.Runtime.InteropServices.Marshal.FreeHGlobal(p_{p.Name}_temp);");
            }
        }
    }

    private static void WriteParameterFree(SourceWriter writer, IParameterSymbol p, bool isDevicePtr)
    {
        if (IsCustomStruct(p.Type))
        {
            writer.AppendLine($"foreach (var ptr in {p.Name}_allocatedPtrs) CudaDriverApi.cuMemFree_v2(ptr);");
            if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
            {
                writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_{p.Name});");
            }

            writer.AppendLine(
                $"if (pArg_{p.Name} != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(pArg_{p.Name});");
        }
        else if (p.Type is IArrayTypeSymbol)
        {
            if (!isDevicePtr)
            {
                writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_{p.Name});");
            }

            writer.AppendLine(
                $"if (pArg_{p.Name} != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(pArg_{p.Name});");
        }
        else if (p.RefKind == RefKind.Ref || p.RefKind == RefKind.Out)
        {
            writer.AppendLine($"if (d_{p.Name} != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_{p.Name});");
            writer.AppendLine(
                $"if (pArg_{p.Name} != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(pArg_{p.Name});");
        }
        else
        {
            writer.AppendLine(
                $"if (pArg_{p.Name} != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(pArg_{p.Name});");
        }
    }
}
