namespace Kassyi.GSharp.Compiler.NativeApi;

using Extensions;
using Helpers;
using Models;

/// <summary>
/// Roslyn source generator that generates boilerplate for invoking CUDA kernels via [CudaImport].
/// </summary>
[Generator]
public class CudaImportGenerator : IIncrementalGenerator
{
    public const string MethodTrackingName = "CudaImportMethod";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var methodDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownAttributes.CudaImportMetadataName,
                predicate: static (s, _) => s is MethodDeclarationSyntax m && HasPartialModifier(m),
                transform: static (ctx, _) => TransformMethod(ctx))
            .WithTrackingName(MethodTrackingName)
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m.GetValueOrDefault());

        var classGroups = methodDeclarations.Collect().SelectMany(static (items, _) =>
        {
            var groups = new Dictionary<string, ImmutableArray<CudaImportMethodModel>.Builder>(StringComparer.Ordinal);
            var order = ImmutableArray.CreateBuilder<string>();

            foreach (var item in items)
            {
                if (!groups.TryGetValue(item.ClassKey, out var builder))
                {
                    builder = ImmutableArray.CreateBuilder<CudaImportMethodModel>();
                    groups[item.ClassKey] = builder;
                    order.Add(item.ClassKey);
                }

                builder.Add(item);
            }

            var result = ImmutableArray.CreateBuilder<CudaImportClassModel>(order.Count);
            foreach (var key in order)
            {
                var builder = groups[key];
                var first = builder[0];
                var lastDot = key.LastIndexOf('.');
                var className = lastDot >= 0 ? key[(lastDot + 1)..] : key;
                result.Add(new CudaImportClassModel(
                    first.NamespaceName,
                    className,
                    first.ClassHeader,
                    builder.ToImmutable().AsEquatableArray()));
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

    private static CudaImportMethodModel? TransformMethod(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol methodSymbol)
        {
            return null;
        }

        var containingClass = methodSymbol.ContainingType;
        if (containingClass == null)
        {
            return null;
        }

        var methodSyntax = (MethodDeclarationSyntax)ctx.TargetNode;

        AttributeData? attributeData = null;
        foreach (var a in ctx.Attributes)
        {
            if (a.IsAttribute(KnownAttributes.CudaImport))
            {
                attributeData = a;
                break;
            }
        }

        if (attributeData == null)
        {
            return null;
        }

        ExtractAttributeArguments(attributeData, methodSyntax, methodSymbol.Name, out var moduleName, out var entryPoint, out var packContext);

        var parameters = CreateLaunchParameters(methodSymbol).AsEquatableArray();

        string namespaceName;
        if (containingClass.ContainingNamespace == null || containingClass.ContainingNamespace.IsGlobalNamespace)
        {
            namespaceName = string.Empty;
        }
        else
        {
            using var nsWriter = new SourceWriter();
            nsWriter.AppendNamespace(containingClass.ContainingNamespace);
            namespaceName = nsWriter.ToString();
        }

        using var classWriter = new SourceWriter();
        EmitClassDeclaration(classWriter, containingClass);
        var classHeader = classWriter.ToString();

        using var sigWriter = new SourceWriter();
        EmitMethodSignature(sigWriter, methodSyntax, methodSymbol);
        var signature = sigWriter.ToString();

        var className = containingClass.Name;
        var classKey = string.IsNullOrEmpty(namespaceName) ? className : $"{namespaceName}.{className}";

        return new CudaImportMethodModel(
            classKey,
            namespaceName,
            classHeader,
            methodSymbol.Name,
            moduleName,
            entryPoint,
            packContext,
            signature,
            parameters);
    }

    private static void GenerateCudaImportForClass(SourceProductionContext context, CudaImportClassModel classGroup)
    {
        using var writer = new SourceWriter();
        EmitFileHeader(writer);

        EmitNamespace(writer, classGroup.NamespaceName);

        writer.AppendLine(classGroup.ClassHeader);
        using (writer.Scope())
        {
            var generatedFatbins = new HashSet<string>();

            foreach (var method in classGroup.Methods)
            {
                if (generatedFatbins.Add(method.ModuleName))
                {
                    ModuleKernelLaunchEmitter.EmitModuleCacheField(writer, method.ModuleName);
                }
            }

            foreach (var method in classGroup.Methods)
            {
                var parameters = method.Parameters.AsImmutableArray();
                if (method.PackContext)
                {
                    ModuleKernelLaunchEmitter.EmitNativeContextStruct(writer, method.MethodName, parameters);
                }

                writer.AppendLine(method.Signature);
                using (writer.Scope())
                {
                    ModuleKernelLaunchEmitter.EmitLaunchBody(writer, method.ModuleName, method.EntryPoint, method.MethodName, parameters, method.PackContext);
                }
            }
        }

        context.AddSource($"{classGroup.ClassName}_CudaImport.g.cs", writer.ToString());
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

    private static void EmitNamespace(SourceWriter writer, string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
        {
            return;
        }

        writer.Append("namespace ");
        writer.Append(namespaceName);
        writer.AppendLine(";");
        writer.AppendLine();
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

        writer.Append($" class {containingClass.Name}");
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

        writer.Append(")");
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
            IdentifierNameSyntax id => KnownAttributes.IsAttributeName(id.Identifier.ValueText, KnownAttributes.CudaImport),
            QualifiedNameSyntax q => KnownAttributes.IsAttributeName(q.Right.Identifier.ValueText, KnownAttributes.CudaImport),
            _ => false
        };
    }

    private static ImmutableArray<ModuleKernelLaunchParameter> CreateLaunchParameters(IMethodSymbol methodSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<ModuleKernelLaunchParameter>(methodSymbol.Parameters.Length);
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

            var hasIn = param.HasAttribute(KnownAttributes.In);
            var hasOut = param.HasAttribute(KnownAttributes.Out);
            builder.Add(new ModuleKernelLaunchParameter(
                param.Name,
                typeWriter.ToString(),
                elementTypeName,
                CopyIn: hasIn || !hasOut,
                CopyOut: hasOut || !hasIn));
        }

        return builder.MoveToImmutable();
    }
}
