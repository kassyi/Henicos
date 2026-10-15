namespace Kassyi.GSharp.Compiler;

using Extensions;
using NativeApi;

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
                    KernelLaunchEmitter.EmitModuleCacheField(writer, moduleName);
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

                var parameters = CreateLaunchParameters(methodSymbol);
                if (packContext)
                {
                    KernelLaunchEmitter.EmitNativeContextStruct(writer, methodSymbol.Name, parameters);
                }

                EmitMethodSignature(writer, methodSyntax, methodSymbol);
                using (writer.Scope())
                {
                    KernelLaunchEmitter.EmitLaunchBody(writer, moduleName, entryPoint, methodSymbol.Name, parameters,
                        packContext);
                }
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

    private static ImmutableArray<KernelLaunchParameter> CreateLaunchParameters(IMethodSymbol methodSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<KernelLaunchParameter>(methodSymbol.Parameters.Length);
        foreach (var param in methodSymbol.Parameters)
        {
            using var typeWriter = new SourceWriter();
            typeWriter.AppendCsharpTypeName(param.Type);

            string? elementTypeName = null;
            if (param.Type is IArrayTypeSymbol arrayType)
            {
                using var elementWriter = new SourceWriter();
                elementWriter.AppendCsharpTypeName(arrayType.ElementType);
                elementTypeName = elementWriter.ToString();
            }

            var hasIn = param.HasAttribute("In");
            var hasOut = param.HasAttribute("Out");
            builder.Add(new KernelLaunchParameter(
                param.Name,
                typeWriter.ToString(),
                elementTypeName,
                CopyIn: hasIn || !hasOut,
                CopyOut: hasOut || !hasIn));
        }

        return builder.MoveToImmutable();
    }
}
