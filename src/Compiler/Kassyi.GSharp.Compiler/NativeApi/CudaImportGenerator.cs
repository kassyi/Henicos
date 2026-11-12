namespace Kassyi.GSharp.Compiler.NativeApi;

using Extensions;
using Helpers;
using Models;

/// <summary>
/// Roslyn source generator that generates boilerplate for invoking CUDA kernels via [CudaImport].
/// </summary>
/// <remarks>
/// Each class with <c>[CudaImport]</c> methods gets one generated part that repeats the declarations of the class and
/// of the types that contain it, and implements each partial method from its symbol. A declaration that such a part
/// cannot complete (<see cref="CudaImportDeclaration.GetProblem"/>, GSH0113) gets nothing.
/// </remarks>
[Generator]
public class CudaImportGenerator : IIncrementalGenerator
{
    public const string MethodTrackingName = "CudaImportMethod";

    private const string HintNameExtension = ".CudaImport.g.cs";

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

        // [WHY] A YAML module binding into the same class declares the field of its module; the methods of that
        // module use it instead of declaring a second one (CS0102)
        var yamlModuleClasses = NativeApiGenerator.GetSourceFiles(context)
            .Select(NativeApiGenerator.GetModuleClasses);

        context.RegisterSourceOutput(classGroups.Combine(yamlModuleClasses), GenerateCudaImportForClass);
    }

    // One model per containing class, in the order the classes first appear, each with its methods in input order
    private static ImmutableArray<CudaImportClassModel> GroupByClass(ImmutableArray<CudaImportMethodModel> items)
    {
        var groups = new Dictionary<string, ImmutableArray<CudaImportMethodModel>.Builder>(StringComparer.Ordinal);
        var order = ImmutableArray.CreateBuilder<string>();

        foreach (var item in items)
        {
            if (!groups.TryGetValue(item.HintName, out var builder))
            {
                builder = ImmutableArray.CreateBuilder<CudaImportMethodModel>();
                groups[item.HintName] = builder;
                order.Add(item.HintName);
            }

            builder.Add(item);
        }

        var result = ImmutableArray.CreateBuilder<CudaImportClassModel>(order.Count);
        foreach (var key in order)
        {
            var builder = groups[key];
            var first = builder[0];
            result.Add(new CudaImportClassModel(
                key,
                first.NamespaceName,
                first.TypeDeclarations,
                first.TopLevelTypeName,
                builder.ToImmutable().AsEquatableArray()));
        }

        return result.ToImmutable();
    }

    private static bool HasPartialModifier(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.PartialKeyword);

    private static CudaImportMethodModel? TransformMethod(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not IMethodSymbol { ContainingType: { } containingType } methodSymbol ||
            CudaImportDeclaration.GetProblem(methodSymbol) is not null)
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

        return new CudaImportMethodModel(
            GlobalCppNames.GetHintName(containingType, HintNameExtension),
            containingType.ContainingNamespace.GetCsharpNamespaceName(),
            CreateTypeDeclarations(containingType),
            GetTopLevelTypeName(containingType),
            CudaImportPlanner.GetMessageName(methodSymbol),
            moduleName,
            entryPoint,
            packContext,
            RenderMethodSignature(methodSyntax, methodSymbol),
            CudaImportPlanner.Plan(methodSymbol, ctx.SemanticModel.Compilation));
    }

    // partial <keyword> <outer>, ..., unsafe partial <keyword> <type>: the declarations of the containing types,
    // outermost first. Each part may leave out the accessibility and the other modifiers of the type; 'unsafe' lets the
    // signature name pointer types whether or not the user's part is unsafe ('ref' must repeat on a ref struct).
    private static EquatableArray<string> CreateTypeDeclarations(INamedTypeSymbol type)
    {
        var declarations = ImmutableArray.CreateBuilder<string>();
        foreach (var outer in HostClassHeader.CreateOuterDeclarations(type))
        {
            declarations.Add(outer);
        }

        var refModifier = type.IsRefLikeType ? "ref " : string.Empty;
        var keyword = HostClassHeader.GetKeyword(type);
        declarations.Add($"unsafe {refModifier}partial {keyword} {type.GetDeclaredTypeName()}");
        return declarations.ToImmutable().AsEquatableArray();
    }

    // <namespace>.<name> of a class that a YAML binding can also extend (not nested, not generic), keywords not escaped
    private static string? GetTopLevelTypeName(INamedTypeSymbol containingClass) =>
        containingClass is { ContainingType: null, TypeParameters.IsEmpty: true }
            ? containingClass.ContainingNamespace.GetMetadataNamespaceName() + "." + containingClass.Name
            : null;

    private static string RenderMethodSignature(MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol)
    {
        using var sigWriter = new SourceWriter();
        EmitMethodSignature(sigWriter, methodSyntax, methodSymbol);
        return sigWriter.ToString();
    }

    private static void GenerateCudaImportForClass(SourceProductionContext context,
        (CudaImportClassModel Class, EquatableArray<YamlModuleClass> YamlModuleClasses) input)
    {
        var (classGroup, yamlModuleClasses) = input;
        using var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        EmitNamespace(writer, classGroup.NamespaceName);
        WriteTypeDeclaration(writer, classGroup, yamlModuleClasses, 0);
        context.AddSource(classGroup.HintName, writer.ToString());
    }

    // <declaration> { <the next declaration, or the members of the class> }
    private static void WriteTypeDeclaration(SourceWriter writer, CudaImportClassModel classGroup,
        EquatableArray<YamlModuleClass> yamlModuleClasses, int depth)
    {
        writer.AppendLine(classGroup.TypeDeclarations[depth]);
        using (writer.Scope())
        {
            if (depth + 1 < classGroup.TypeDeclarations.Length)
            {
                WriteTypeDeclaration(writer, classGroup, yamlModuleClasses, depth + 1);
            }
            else
            {
                WriteMembers(writer, classGroup, yamlModuleClasses);
            }
        }
    }

    // One field per module and class (named by the module, ModuleKernelLaunchEmitter.GetModuleFieldName), unless a
    // YAML binding of the class declares it, then the methods
    private static void WriteMembers(SourceWriter writer, CudaImportClassModel classGroup,
        EquatableArray<YamlModuleClass> yamlModuleClasses)
    {
        var modules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in classGroup.Methods)
        {
            if (modules.Add(method.ModuleName) &&
                !IsDeclaredByYaml(yamlModuleClasses, classGroup.TopLevelTypeName, method.ModuleName))
            {
                ModuleKernelLaunchEmitter.EmitModuleCacheField(writer,
                    ModuleKernelLaunchEmitter.GetModuleFieldName(method.ModuleName), method.ModuleName);
            }
        }

        for (var i = 0; i < classGroup.Methods.Length; i++)
        {
            EmitMethod(writer, classGroup.Methods[i], i);
        }
    }

    // Whether a YAML module binding declares the field of <moduleName> in the class <typeName>
    private static bool IsDeclaredByYaml(EquatableArray<YamlModuleClass> yamlModuleClasses, string? typeName,
        string moduleName) =>
        typeName is not null &&
        yamlModuleClasses
            .AsStackEnumerable()
            .Any(new YamlModuleClass(typeName, moduleName), static (c, key) => c == key);

    // A method whose parameters the launcher cannot pass gets a body that throws the GSH0110 message, so the partial
    // method stays implemented and the build shows the diagnostic instead of CS8795
    private static void EmitMethod(SourceWriter writer, CudaImportMethodModel method, int index)
    {
        var moduleField = ModuleKernelLaunchEmitter.GetModuleFieldName(method.ModuleName);
        var plan = method.Plan;
        var contextTypeName = method.PackContext && plan.CanLaunch
            ? ModuleKernelLaunchEmitter.GetNativeContextTypeName(index)
            : null;
        if (contextTypeName is not null)
        {
            ModuleKernelLaunchEmitter.EmitNativeContextStruct(writer, contextTypeName, plan);
        }

        writer.AppendLine(method.Signature);
        using (writer.Scope())
        {
            if (plan.CanLaunch)
            {
                ModuleKernelLaunchEmitter.EmitLaunchBody(writer, moduleField, method.EntryPoint, method.MessageName,
                    plan, contextTypeName);
            }
            else
            {
                ModuleKernelLaunchEmitter.EmitUnavailableBody(writer,
                    CudaImportPlanner.FormatProblem(method.MessageName, plan.Problems[0]));
            }
        }
    }

    private static void EmitNamespace(SourceWriter writer, string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
        {
            return;
        }

        writer.AppendLine($"namespace {namespaceName};");
        writer.AppendLine();
    }

    // <modifiers> void <name>(<modifiers> <type> <name>, ...), names escaped and types from global::
    private static void EmitMethodSignature(SourceWriter writer, MethodDeclarationSyntax methodSyntax,
        IMethodSymbol methodSymbol)
    {
        foreach (var modifier in methodSyntax.Modifiers)
        {
            writer.Append(modifier.Text);
            writer.Append(' ');
        }

        writer.Append("void ");
        writer.AppendCsharpIdentifier(methodSymbol.Name);
        using (writer.Parentheses())
        {
            for (var i = 0; i < methodSymbol.Parameters.Length; i++)
            {
                var param = methodSymbol.Parameters[i];
                writer.AppendIf(i > 0, ", ");

                // [WHY] The implementation repeats the modifiers of the declaration ('params', 'scoped', 'this', the
                // ref kind); a partial method whose two parts differ in them does not compile (CS0758, CS0759, CS8988)
                foreach (var modifier in methodSyntax.ParameterList.Parameters[i].Modifiers)
                {
                    writer.Append($"{modifier.Text} ");
                }

                writer.AppendCsharpTypeName(param.Type);
                writer.Append(' ');
                writer.AppendCsharpIdentifier(param.Name);
            }
        }
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
