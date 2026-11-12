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

        var classGroups = methodDeclarations
            .Collect()
            .SelectMany(static (items, _) => GroupByClass(items));

        context.RegisterSourceOutput(classGroups, GenerateCudaImportForClass);
    }

    // One model per containing class, in the order the classes first appear, each with its methods in input order
    private static ImmutableArray<CudaImportClassModel> GroupByClass(ImmutableArray<CudaImportMethodModel> items)
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
    }

    private static bool HasPartialModifier(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.PartialKeyword);

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

        var attributeData = ctx.Attributes
            .AsStackEnumerable()
            .FirstOrDefault(static a => a.IsAttribute(KnownAttributes.CudaImport));

        if (attributeData == null)
        {
            return null;
        }

        ExtractAttributeArguments(attributeData, methodSyntax, methodSymbol.Name,
            out var moduleName, out var entryPoint, out var packContext);

        var plan = CudaImportPlanner.Plan(methodSymbol, ctx.SemanticModel.Compilation);
        var namespaceName = GetNamespaceName(containingClass);
        var classHeader = RenderClassHeader(containingClass);
        var signature = RenderMethodSignature(methodSyntax, methodSymbol);
        var className = containingClass.Name;
        var classKey = string.IsNullOrEmpty(namespaceName) ? className : $"{namespaceName}.{className}";

        return new CudaImportMethodModel(
            classKey,
            namespaceName,
            classHeader,
            methodSymbol.Name,
            CudaImportPlanner.GetMessageName(methodSymbol),
            moduleName,
            entryPoint,
            packContext,
            signature,
            plan);
    }

    private static string GetNamespaceName(INamedTypeSymbol containingClass)
    {
        if (containingClass.ContainingNamespace == null || containingClass.ContainingNamespace.IsGlobalNamespace)
        {
            return string.Empty;
        }

        using var nsWriter = new SourceWriter();
        nsWriter.AppendNamespace(containingClass.ContainingNamespace);
        return nsWriter.ToString();
    }

    private static string RenderClassHeader(INamedTypeSymbol containingClass)
    {
        using var classWriter = new SourceWriter();
        EmitClassDeclaration(classWriter, containingClass);
        return classWriter.ToString();
    }

    private static string RenderMethodSignature(MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol)
    {
        using var sigWriter = new SourceWriter();
        EmitMethodSignature(sigWriter, methodSyntax, methodSymbol);
        return sigWriter.ToString();
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
                EmitMethod(writer, method);
            }
        }

        context.AddSource($"{classGroup.ClassName}_CudaImport.g.cs", writer.ToString());
    }

    // A method whose parameters the launcher cannot pass gets a body that throws the GSH0110 message, so the partial
    // method stays implemented and the build shows the diagnostic instead of CS8795
    private static void EmitMethod(SourceWriter writer, CudaImportMethodModel method)
    {
        var plan = method.Plan;
        if (method.PackContext && plan.CanLaunch)
        {
            ModuleKernelLaunchEmitter.EmitNativeContextStruct(writer, method.MethodName, plan);
        }

        writer.AppendLine(method.Signature);
        using (writer.Scope())
        {
            if (plan.CanLaunch)
            {
                ModuleKernelLaunchEmitter.EmitLaunchBody(writer, method.ModuleName, method.EntryPoint,
                    method.MethodName, method.MessageName, plan, method.PackContext);
            }
            else
            {
                ModuleKernelLaunchEmitter.EmitUnavailableBody(writer,
                    CudaImportPlanner.FormatProblem(method.MessageName, plan.Problems[0]));
            }
        }
    }

    private static void EmitFileHeader(SourceWriter writer)
    {
        writer.AppendGeneratedFileHeader();
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
                    writer.Append(' ');
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
            writer.Append(' ');
        }

        writer.Append($"void {methodSymbol.Name}(");
        for (var i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            var param = methodSymbol.Parameters[i];

            // [WHY] The implementation repeats the modifiers of the declaration ('params', 'scoped', 'this', the ref
            // kind); a partial method whose two parts differ in them does not compile (CS0758, CS0759, CS8988)
            foreach (var modifier in methodSyntax.ParameterList.Parameters[i].Modifiers)
            {
                writer.Append($"{modifier.Text} ");
            }

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
        moduleName = ExtractModuleName(attributeData);
        entryPoint = ReadNamedArguments(attributeData, out packContext) ??
                     ResolveEntryPointFromSyntax(methodSyntax, defaultEntryPoint);
    }

    private static string ExtractModuleName(AttributeData attributeData)
    {
        if (attributeData.ConstructorArguments.Length > 0 &&
            attributeData.ConstructorArguments[0].Value is string argValue)
        {
            return argValue;
        }

        return "unknown_module";
    }

    // The EntryPoint the semantic model resolved (null when it could not), and PackContext
    private static string? ReadNamedArguments(AttributeData attributeData, out bool packContext)
    {
        string? entryPoint = null;
        packContext = false;

        foreach (var namedArg in attributeData.NamedArguments)
        {
            if (namedArg is { Key: "EntryPoint", Value.Value: string argValue })
            {
                entryPoint = argValue;
            }
            else if (namedArg is { Key: "PackContext", Value.Value: bool boolValue })
            {
                packContext = boolValue;
            }
        }

        return entryPoint;
    }

    private static string ResolveEntryPointFromSyntax(MethodDeclarationSyntax methodSyntax, string fallback)
    {
        // Fallback to syntax tree parsing if the constant couldn't be resolved by the SemanticModel
        // (e.g. because it is generated by another generator in the same pass)
        var attributeSyntax = FindCudaImportAttribute(methodSyntax);
        if (attributeSyntax?.ArgumentList == null)
        {
            return fallback;
        }

        if (!attributeSyntax.ArgumentList.Arguments
                .AsStackEnumerable()
                .TryFirst(static arg => arg.NameEquals?.Name.Identifier.ValueText == "EntryPoint", out var arg))
        {
            return fallback;
        }

        return arg.Expression switch
        {
            LiteralExpressionSyntax entryLit when entryLit.IsKind(SyntaxKind.StringLiteralExpression) =>
                entryLit.Token.ValueText,
            InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                ArgumentList.Arguments.Count: > 0
            } nameofInvocation when
                nameofInvocation.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax targetId =>
                targetId.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax idSyntax => idSyntax.Identifier.ValueText,
            _ => fallback
        };
    }

    private static AttributeSyntax? FindCudaImportAttribute(MethodDeclarationSyntax methodSyntax) =>
        methodSyntax.AttributeLists
            .AsStackEnumerable()
            .SelectMany(static list => list.Attributes
                .AsStackEnumerable()
            ).FirstOrDefault(static attribute => IsCudaImportAttributeName(attribute.Name));

    private static bool IsCudaImportAttributeName(NameSyntax name)
    {
        return name switch
        {
            IdentifierNameSyntax id => KnownAttributes.IsAttributeName(id.Identifier.ValueText, KnownAttributes.CudaImport),
            QualifiedNameSyntax q => KnownAttributes.IsAttributeName(q.Right.Identifier.ValueText, KnownAttributes.CudaImport),
            _ => false
        };
    }
}
