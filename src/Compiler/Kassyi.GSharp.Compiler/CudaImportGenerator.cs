namespace Kassyi.GSharp.Compiler;

using Extensions;

/// <summary>
/// Roslyn source generator that generates boilerplate for invoking CUDA kernels via [CudaImport].
/// </summary>
[Generator]
public class CudaImportGenerator : IIncrementalGenerator
{
    private const string CudaImportAttribute = "CudaImportAttribute";
    private const string AttributeMetadataName = "Kassyi.GSharp.Core.CudaImportAttribute";
    private const string AttributeShortName = "CudaImport";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methodDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                predicate: static (s, _) => s is MethodDeclarationSyntax m && HasPartialModifier(m),
                transform: static (ctx, _) => ctx)
            .Where(static m => m.TargetSymbol is IMethodSymbol);

        var classGroups = methodDeclarations.Collect().SelectMany(static (items, _) =>
        {
            var groups =
                new Dictionary<INamedTypeSymbol, ImmutableArray<GeneratorAttributeSyntaxContext>.Builder>(
                    SymbolEqualityComparer.Default);
            foreach (var item in items)
            {
                var type = item.TargetSymbol.ContainingType;
                if (!groups.TryGetValue(type, out var builder))
                {
                    builder = ImmutableArray.CreateBuilder<GeneratorAttributeSyntaxContext>();
                    groups[type] = builder;
                }

                builder.Add(item);
            }

            var result = ImmutableArray
                .CreateBuilder<KeyValuePair<INamedTypeSymbol, ImmutableArray<GeneratorAttributeSyntaxContext>>>();
            foreach (var kvp in groups)
            {
                result.Add(new KeyValuePair<INamedTypeSymbol, ImmutableArray<GeneratorAttributeSyntaxContext>>(kvp.Key,
                    kvp.Value.ToImmutable()));
            }

            return result.ToImmutable();
        });

        context.RegisterSourceOutput(classGroups, GenerateCudaImportForClass);
    }

    private static bool HasPartialModifier(MethodDeclarationSyntax method)
    {
        foreach (var modifier in method.Modifiers)
        {
            if (modifier.IsKind(SyntaxKind.PartialKeyword))
            {
                return true;
            }
        }

        return false;
    }

    private static void GenerateCudaImportForClass(SourceProductionContext context,
        KeyValuePair<INamedTypeSymbol, ImmutableArray<GeneratorAttributeSyntaxContext>> group)
    {
        var containingClass = group.Key;
        var contexts = group.Value;

        using var writer = new SourceWriter();
        EmitFileHeader(writer);

        EmitNamespace(writer, containingClass.ContainingNamespace);

        EmitClassDeclaration(writer, containingClass);
        using (writer.Scope())
        {
            var generatedFatbins = new HashSet<string>();

            foreach (var ctx in contexts)
            {
                var methodSymbol = (IMethodSymbol)ctx.TargetSymbol;
                var methodSyntax = (MethodDeclarationSyntax)ctx.TargetNode;
                AttributeData? attributeData = null;
                foreach (var a in ctx.Attributes)
                {
                    if (a.AttributeClass?.Name != CudaImportAttribute)
                    {
                        continue;
                    }

                    attributeData = a;
                    break;
                }
                if (attributeData == null)
                {
                    continue;
                }

                ExtractAttributeArguments(attributeData, methodSyntax, methodSymbol.Name, out var moduleName, out var _,
                    out var _);

                if (generatedFatbins.Add(moduleName))
                {
                    EmitModuleCacheField(writer, moduleName);
                }
            }

            foreach (var ctx in contexts)
            {
                var methodSymbol = (IMethodSymbol)ctx.TargetSymbol;
                var methodSyntax = (MethodDeclarationSyntax)ctx.TargetNode;

                AttributeData? attributeData = null;
                foreach (var a in ctx.Attributes)
                {
                    if (a.AttributeClass?.Name != CudaImportAttribute)
                    {
                        continue;
                    }

                    attributeData = a;
                    break;
                }
                if (attributeData == null)
                {
                    continue;
                }

                ExtractAttributeArguments(attributeData, methodSyntax, methodSymbol.Name, out var moduleName,
                    out var entryPoint, out var packContext);

                if (packContext)
                {
                    EmitNativeContextStruct(writer, methodSymbol);
                }

                EmitMethodImplementation(writer, methodSyntax, methodSymbol, moduleName, entryPoint, packContext);
            }
        }

        context.AddSource($"{containingClass.Name}_CudaImport.g.cs", writer.ToString());
    }

    private static void EmitFileHeader(SourceWriter writer)
    {
        writer.AppendLine("// <auto-generated/>");
        writer.AppendLine("#nullable enable");
        writer.AppendLine("using System;");
        writer.AppendLine("using System.Runtime.InteropServices;");
        writer.AppendLine("using Kassyi.GSharp.Runtime;");
        writer.AppendLine("using Kassyi.GSharp.Runtime.Interop;");
        writer.AppendLine();
    }

    private static void EmitNamespace(SourceWriter writer, INamespaceSymbol? namespaceSymbol)
    {
        if (namespaceSymbol == null || namespaceSymbol.IsGlobalNamespace)
        {
            return;
        }

        writer.Append("namespace ");
        EmitNamespaceRecursive(writer, namespaceSymbol);
        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void EmitNamespaceRecursive(SourceWriter writer, INamespaceSymbol namespaceSymbol)
    {
        if (namespaceSymbol.ContainingNamespace is not null && !namespaceSymbol.ContainingNamespace.IsGlobalNamespace)
        {
            EmitNamespaceRecursive(writer, namespaceSymbol.ContainingNamespace);
            writer.Append('.');
        }

        writer.Append(namespaceSymbol.Name);
    }

    private static void EmitClassDeclaration(SourceWriter writer, INamedTypeSymbol containingClass)
    {
        var hasPartial = false;
        var hasAnyModifier = false;

        foreach (var reference in containingClass.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not TypeDeclarationSyntax typeDecl)
            {
                continue;
            }

            foreach (var modifier in typeDecl.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.PartialKeyword))
                {
                    hasPartial = true;
                }

                if (hasAnyModifier)
                {
                    writer.AppendSpace();
                }

                writer.Append(modifier.Text);
                hasAnyModifier = true;
            }

            break;
        }

        if (!hasAnyModifier)
        {
            writer.Append("public partial");
        }
        else if (!hasPartial)
        {
            writer.Append(" partial");
        }

        writer.AppendLine($" class {containingClass.Name}");
    }

    private static void EmitModuleCacheField(SourceWriter writer, string moduleName)
    {
        writer.Append("private static readonly Lazy<byte[]> s_fatbin_");
        AppendSanitizedIdentifier(writer, moduleName);
        writer.AppendLine(
            $" = new(() => System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, \"{moduleName}\")));");
        writer.AppendLine();
    }

    private static void EmitNativeContextStruct(SourceWriter writer, IMethodSymbol methodSymbol)
    {
        writer.AppendLine("[StructLayout(LayoutKind.Sequential)]");
        writer.AppendLine($"private struct __NativeContext_{methodSymbol.Name}");
        using (writer.Scope())
        {
            foreach (var param in methodSymbol.Parameters)
            {
                if (IsLaunchParameter(param.Name))
                {
                    continue;
                }

                if (param.Type is IArrayTypeSymbol)
                {
                    writer.AppendLine($"public IntPtr {param.Name};");
                }
                else
                {
                    writer.Append("public ");
                    writer.AppendCsharpTypeName(param.Type);
                    writer.AppendLine($" {param.Name};");
                }
            }
        }

        writer.AppendLine();
    }


    private static void EmitMethodImplementation(
        SourceWriter writer,
        MethodDeclarationSyntax methodSyntax,
        IMethodSymbol methodSymbol,
        string moduleName,
        string entryPoint,
        bool packContext)
    {
        EmitMethodSignature(writer, methodSyntax, methodSymbol);
        using (writer.Scope())
        {
            writer.AppendLine("using var scope = new GpuKernelLoader.CudaContextScope();");
            writer.Append("var kernel = GpuKernelLoader.LoadAotKernel(s_fatbin_");
            AppendSanitizedIdentifier(writer, moduleName);
            writer.AppendLine($".Value, \"{entryPoint}\");");
            writer.AppendLine();

            EmitBufferAllocations(writer, methodSymbol);
            if (packContext)
            {
                EmitContextInitialization(writer, methodSymbol);
            }

            EmitKernelLaunch(writer, methodSymbol, packContext);
            EmitBufferOutputs(writer, methodSymbol);
        }
    }

    private static void EmitMethodSignature(SourceWriter writer, MethodDeclarationSyntax methodSyntax,
        IMethodSymbol methodSymbol)
    {
        foreach (var modifier in methodSyntax.Modifiers)
        {
            writer.Append(modifier.Text);
            writer.AppendSpace();
        }

        writer.Append($"void {methodSymbol.Name}(");
        for (var i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            var param = methodSymbol.Parameters[i];
            writer.AppendCsharpTypeName(param.Type);
            writer.Append($" {param.Name}");
            if (i < methodSymbol.Parameters.Length - 1)
            {
                writer.Append(", ");
            }
        }

        writer.AppendLine(")");
    }

    private static void EmitBufferAllocations(SourceWriter writer, IMethodSymbol methodSymbol)
    {
        foreach (var param in methodSymbol.Parameters)
        {
            if (IsLaunchParameter(param.Name))
            {
                continue;
            }

            if (param.Type is not IArrayTypeSymbol arrayType)
            {
                continue;
            }

            writer.Append($"using var d_{param.Name} = new GpuBuffer<");
            writer.AppendCsharpTypeName(arrayType.ElementType);
            writer.AppendLine($">({param.Name}.Length);");

            var hasIn = param.HasAttribute("In");
            var hasOut = param.HasAttribute("Out");

            if (hasIn || (!hasIn && !hasOut))
            {
                writer.AppendLine($"d_{param.Name}.CopyToDevice({param.Name});");
            }
        }

        writer.AppendLine();
    }


    private static void EmitContextInitialization(SourceWriter writer, IMethodSymbol methodSymbol)
    {
        writer.AppendLine($"var ctx = new __NativeContext_{methodSymbol.Name}");
        using (writer.Scope())
        {
            for (var i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                var param = methodSymbol.Parameters[i];
                if (IsLaunchParameter(param.Name))
                {
                    continue;
                }

                if (param.Type is IArrayTypeSymbol)
                {
                    writer.Append($"{param.Name} = d_{param.Name}.DevicePointer");
                }
                else
                {
                    writer.Append($"{param.Name} = {param.Name}");
                }

                var isLast = true;
                for (var j = i + 1; j < methodSymbol.Parameters.Length; j++)
                {
                    if (!IsLaunchParameter(methodSymbol.Parameters[j].Name))
                    {
                        isLast = false;
                        break;
                    }
                }

                if (!isLast)
                {
                    writer.AppendLine(",");
                }
                else
                {
                    writer.AppendLine();
                }
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void EmitKernelLaunch(SourceWriter writer, IMethodSymbol methodSymbol, bool packContext)
    {
        var kernelArgsCount = 0;
        foreach (var param in methodSymbol.Parameters)
        {
            if (!IsLaunchParameter(param.Name))
            {
                kernelArgsCount++;
            }
        }

        writer.AppendLine("unsafe");
        using (writer.Scope())
        {
            if (packContext)
            {
                writer.AppendLine("var kernelParams = stackalloc void*[1];");
                writer.AppendLine("kernelParams[0] = &ctx;");
            }
            else
            {
                foreach (var param in methodSymbol.Parameters)
                {
                    if (IsLaunchParameter(param.Name))
                    {
                        continue;
                    }
                    if (param.Type is IArrayTypeSymbol)
                    {
                        writer.AppendLine($"var p_{param.Name} = d_{param.Name}.DevicePointer;");
                    }

                    else
                    {
                        writer.AppendLine($"var p_{param.Name} = {param.Name};");
                    }
                }

                if (kernelArgsCount > 0)
                {
                    writer.AppendLine($"var kernelParams = stackalloc void*[{kernelArgsCount}];");
                    var paramIndex = 0;
                    foreach (var param in methodSymbol.Parameters)
                    {
                        if (IsLaunchParameter(param.Name))
                        {
                            continue;
                        }

                        writer.AppendLine($"kernelParams[{paramIndex}] = &p_{param.Name};");
                        paramIndex++;
                    }
                }
                else
                {
                    writer.AppendLine("void** kernelParams = null;");
                }
            }

            writer.AppendLine();

            var gridX = GetLaunchParamOrValue(methodSymbol, "gridDimX", "1");
            var gridY = GetLaunchParamOrValue(methodSymbol, "gridDimY", "1");
            var gridZ = GetLaunchParamOrValue(methodSymbol, "gridDimZ", "1");
            var blockX = GetLaunchParamOrValue(methodSymbol, "blockDimX", "1");
            var blockY = GetLaunchParamOrValue(methodSymbol, "blockDimY", "1");
            var blockZ = GetLaunchParamOrValue(methodSymbol, "blockDimZ", "1");
            var sharedMem = GetLaunchParamOrValue(methodSymbol, "sharedMemBytes", "0");
            var stream = GetLaunchParamOrValue(methodSymbol, "stream", "IntPtr.Zero");

            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(");
            writer.AppendLine("kernel,");
            writer.AppendLine($"{gridX}, {gridY}, {gridZ},");
            writer.AppendLine($"{blockX}, {blockY}, {blockZ},");
            writer.AppendLine($"{sharedMem}, {stream}, kernelParams, IntPtr.Zero));");
        }

        writer.AppendLine();
        writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());");
    }

    private static void EmitBufferOutputs(SourceWriter writer, IMethodSymbol methodSymbol)
    {
        foreach (var param in methodSymbol.Parameters)
        {
            if (IsLaunchParameter(param.Name))
            {
                continue;
            }

            if (param.Type is not IArrayTypeSymbol)
            {
                continue;
            }

            var hasIn = param.HasAttribute("In");
            var hasOut = param.HasAttribute("Out");

            if (hasOut || (!hasIn && !hasOut))
            {
                writer.AppendLine($"d_{param.Name}.CopyToHost({param.Name});");
            }
        }
    }


    private static bool IsLaunchParameter(string name) =>
        name is "gridDimX" or "gridDimY" or "gridDimZ"
            or "blockDimX" or "blockDimY" or "blockDimZ"
            or "sharedMemBytes" or "stream";

    private static string GetLaunchParamOrValue(IMethodSymbol method, string name, string defaultValue)
    {
        foreach (var p in method.Parameters)
        {
            if (p.Name == name)
            {
                return name;
            }
        }

        return defaultValue;
    }

    private static void ExtractAttributeArguments(
        AttributeData attributeData,
        MethodDeclarationSyntax methodSyntax,
        string defaultEntryPoint,
        out string moduleName,
        out string entryPoint,
        out bool packContext)
    {
        moduleName = "unknown_module";
        entryPoint = defaultEntryPoint;
        packContext = false;

        if (attributeData.ConstructorArguments.Length > 0)
        {
            if (attributeData.ConstructorArguments[0].Value is string argValue)
            {
                moduleName = argValue;
            }
        }

        var entryPointResolved = false;
        foreach (var namedArg in attributeData.NamedArguments)
        {
            if (namedArg.Key == "EntryPoint")
            {
                if (namedArg.Value.Value is not string argValue)
                {
                    continue;
                }

                entryPoint = argValue;
                entryPointResolved = true;
            }
            else if (namedArg is { Key: "PackContext", Value.Value: bool boolValue })
            {
                packContext = boolValue;
            }
        }

        if (!entryPointResolved)
        {
            // Fallback to syntax tree parsing if the constant couldn't be resolved by the SemanticModel
            // (e.g. because it is generated by another generator in the same pass)
            var attributeSyntax = FindCudaImportAttribute(methodSyntax);
            if (attributeSyntax?.ArgumentList == null)
            {
                return;
            }

            foreach (var arg in attributeSyntax.ArgumentList.Arguments)
            {
                if (arg.NameEquals?.Name.Identifier.ValueText != "EntryPoint")
                {
                    continue;
                }

                entryPoint = arg.Expression switch
                {
                    LiteralExpressionSyntax entryLit when entryLit.IsKind(SyntaxKind.StringLiteralExpression) =>
                        entryLit.Token.ValueText,
                    InvocationExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                        ArgumentList.Arguments.Count: > 0
                    } nameofInvocation when
                        nameofInvocation.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax targetId =>
                        targetId
                            .Identifier.ValueText,
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                    IdentifierNameSyntax idSyntax => idSyntax.Identifier.ValueText,
                    _ => entryPoint
                };

                break;
            }
        }
    }

    private static AttributeSyntax? FindCudaImportAttribute(MethodDeclarationSyntax methodSyntax)
    {
        foreach (var attributeList in methodSyntax.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                if (IsCudaImportAttributeName(attribute.Name))
                {
                    return attribute;
                }
            }
        }

        return null;
    }

    private static bool IsCudaImportAttributeName(NameSyntax name)
    {
        return name switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText is AttributeShortName or nameof(CudaImportAttribute),
            QualifiedNameSyntax q => q.Right.Identifier.ValueText is AttributeShortName or nameof(CudaImportAttribute),
            _ => false
        };
    }

    private static void AppendSanitizedIdentifier(SourceWriter writer, string name)
    {
        foreach (var c in name)
        {
            writer.Append(c is '.' or '-' ? '_' : c);
        }
    }
}
