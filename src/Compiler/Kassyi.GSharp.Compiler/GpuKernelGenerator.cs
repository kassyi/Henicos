using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Naming;

namespace Kassyi.GSharp.Compiler;

/// <summary>Roslyn incremental source generator that transpiles C# GPU kernels to CUDA C++ and emits host invocations.</summary>
[Generator]
public class GpuKernelGenerator : IIncrementalGenerator
{
    private sealed class DiagnosticCollector
    {
        private readonly ImmutableArray<Diagnostic>.Builder _diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

        public EquatableArray<EquatableDiagnostic> GetDiagnostics()
        {
            var builder = ImmutableArray.CreateBuilder<EquatableDiagnostic>(_diagnostics.Count);
            foreach (var d in _diagnostics)
            {
                builder.Add(d.AsEquatable());
            }

            return builder.MoveToImmutable().AsEquatableArray();
        }

        public void Report(Diagnostic d)
        {
            foreach (var existing in _diagnostics)
            {
                if (existing.Id == d.Id && existing.Location == d.Location)
                {
                    return;
                }
            }

            _diagnostics.Add(d);
        }
    }

    private static bool CheckSuppressGs0001(MethodDeclarationSyntax methodDeclaration)
    {
        var root = methodDeclaration.SyntaxTree.GetRoot();
        if (!root.ContainsDirectives)
        {
            return false;
        }

        var suppressGs0001 = false;
        var nodes = root.DescendantNodes(TextSpan.FromBounds(0, methodDeclaration.Span.End), descendIntoTrivia: true);

        foreach (var node in nodes)
        {
            if (node is not PragmaWarningDirectiveTriviaSyntax pragma)
            {
                continue;
            }

            foreach (var errorCode in pragma.ErrorCodes)
            {
                if (errorCode is not IdentifierNameSyntax id || id.Identifier.ValueText != "GS0001")
                {
                    continue;
                }

                if (pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
                {
                    suppressGs0001 = true;
                }
                else if (pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.RestoreKeyword))
                {
                    suppressGs0001 = false;
                }
            }
        }

        return suppressGs0001;
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var configProvider = context.AnalyzerConfigOptionsProvider
            .Select((options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.GSharpTargetDialect", out var dialect);
                var finalDialect = string.IsNullOrWhiteSpace(dialect) ? "Cpu" : dialect;

                options.GlobalOptions.TryGetValue("build_property.GSharpEmitSource", out var emitSrcStr);
                var emitSource = string.IsNullOrWhiteSpace(emitSrcStr) ||
                                 string.Equals(emitSrcStr, "true", StringComparison.OrdinalIgnoreCase);

                options.GlobalOptions.TryGetValue("build_property.IntermediateOutputPath", out var objPath);
                options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var projDir);
                if (string.IsNullOrWhiteSpace(projDir))
                {
                    options.GlobalOptions.TryGetValue("build_property.ProjectDir", out projDir);
                }

                var intermediatePath = !string.IsNullOrWhiteSpace(objPath) && !string.IsNullOrWhiteSpace(projDir)
                    ? Path.Combine(projDir, objPath)
                    : null;

                return new GeneratorConfig(finalDialect ?? "Cpu", emitSource, intermediatePath);
            });

        var kernelMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuKernel),
                transform: static (ctx, _) => TranspileKernel(ctx, KnownAttributes.GpuKernel))
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m.GetValueOrDefault());

        var deviceMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuDeviceFunction),
                transform: static (ctx, _) => TranspileDeviceMethod(ctx, KnownAttributes.GpuDeviceFunction))
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m.GetValueOrDefault());

        var kernelsAndDevices = kernelMethods.Collect().Combine(deviceMethods.Collect());
        var compilationAndAll = context.CompilationProvider.Combine(kernelsAndDevices.Combine(configProvider));

        context.RegisterSourceOutput(compilationAndAll,
            static (spc, source) => Execute(source.Left, source.Right.Left.Left, source.Right.Left.Right,
                source.Right.Right, spc));
    }

    private static TranspiledKernelResult? TranspileKernel(GeneratorSyntaxContext context, string attributeName)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDeclaration);
        if (methodSymbol == null || !methodSymbol.HasAttribute(attributeName))
        {
            return null;
        }

        try
        {
            var collector = new DiagnosticCollector();
            var cudaDialect = new GSharpCudaDialect();
            var cppDialect = new StandardCppDialect();

            var cudaSource = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel,
                collector.Report,
                isDevice: false, cudaDialect, out var requiredIncludesCuda);

            var lowered = methodDeclaration.HasBody()
                ? AstLoweringPipeline.Lower(methodDeclaration, context.SemanticModel)
                : null;

            var cppSource = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel, _ =>
            {
            }, isDevice: false, cppDialect, out var requiredIncludesCpp, lowered);

            var managedCSharpSource =
                MethodTranspiler.GenerateManagedCSharpSource(methodDeclaration, context.SemanticModel, lowered);

            if (!CheckSuppressGs0001(methodDeclaration))
            {
                return new TranspiledKernelResult(
                    methodSymbol,
                    methodSymbol.Name,
                    methodSymbol.ContainingType.Name,
                    methodSymbol.ContainingNamespace?.GetFullNamespaceName(),
                    cudaSource,
                    cppSource,
                    managedCSharpSource,
                    new EquatableArray<string>(CombineIncludes(requiredIncludesCuda, requiredIncludesCpp)),
                    collector.GetDiagnostics()
                );
            }

            cudaSource = "// gsharp:suppress GS0001\n" + cudaSource;
            cppSource = "// gsharp:suppress GS0001\n" + cppSource;

            return new TranspiledKernelResult(
                methodSymbol,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name,
                methodSymbol.ContainingNamespace?.GetFullNamespaceName(),
                cudaSource,
                cppSource,
                managedCSharpSource,
                new EquatableArray<string>(CombineIncludes(requiredIncludesCuda, requiredIncludesCpp)),
                collector.GetDiagnostics()
            );
        }
        catch (Exception ex)
        {
            var collector = new DiagnosticCollector();
            collector.Report(Diagnostic.Create(
                GSharpDiagnostics.InternalCompilerError,
                Location.None,
                ex.ToString()));

            return new TranspiledKernelResult(
                methodSymbol,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name,
                methodSymbol.ContainingNamespace?.GetFullNamespaceName(),
                "", "", $"/*\n{ex}\n*/\n#error \"Internal Compiler Error: Check comments for details\"\n",
                new EquatableArray<string>(), collector.GetDiagnostics());
        }
    }

    private static TranspiledDeviceMethodResult? TranspileDeviceMethod(GeneratorSyntaxContext context,
        string attributeName)
    {
        var methodDeclaration = (MethodDeclarationSyntax)context.Node;
        var methodSymbol = context.SemanticModel.GetDeclaredSymbol<IMethodSymbol>(methodDeclaration);
        if (methodSymbol == null || !methodSymbol.HasAttribute(attributeName))
        {
            return null;
        }

        try
        {
            var collector = new DiagnosticCollector();
            var cudaDialect = new GSharpCudaDialect();
            var cppDialect = new StandardCppDialect();

            var cudaSignature = MethodTranspiler.GenerateCudaSignature(methodDeclaration, context.SemanticModel,
                collector.Report, isDevice: true, cudaDialect);
            var cppSignature = MethodTranspiler.GenerateCudaSignature(methodDeclaration, context.SemanticModel, _ =>
            {
            }, isDevice: true, cppDialect);

            var cudaBody = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel,
                collector.Report,
                isDevice: true, cudaDialect, out var requiredIncludesCuda);
            var cppBody = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel, _ =>
            {
            }, isDevice: true, cppDialect, out var requiredIncludesCpp);

            // A device function that may suspend has a frame, which embeds the frames of the calls it makes
            var frameName = "";
            var frameDependencies = ImmutableArray.CreateBuilder<string>();
            var managedCSharpSource = "";
            if (methodDeclaration.GetBodyNode() is { } body &&
                SuspensionAnalysis.RequiresStateMachine(methodSymbol, context.SemanticModel.Compilation))
            {
                frameName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.FrameTypePrefix, methodSymbol);
                foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body,
                             context.SemanticModel.Compilation))
                {
                    frameDependencies.Add(GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.FrameTypePrefix, callee));
                }

                managedCSharpSource =
                    MethodTranspiler.GenerateManagedDeviceFunctionSource(methodDeclaration, context.SemanticModel);
            }

            return new TranspiledDeviceMethodResult(
                methodSymbol,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name,
                methodSymbol.ContainingNamespace?.GetFullNamespaceName(),
                cudaSignature,
                cudaBody,
                cppSignature,
                cppBody,
                new EquatableArray<string>(CombineIncludes(requiredIncludesCuda, requiredIncludesCpp)),
                collector.GetDiagnostics(),
                frameName,
                new EquatableArray<string>(frameDependencies.ToImmutable()),
                managedCSharpSource);
        }
        catch (Exception ex)
        {
            var collector = new DiagnosticCollector();
            collector.Report(Diagnostic.Create(
                GSharpDiagnostics.InternalCompilerError,
                Location.None,
                ex.ToString()));

            var errorSource = $"/*\n{ex}\n*/\n#error \"Internal Compiler Error: Check comments for details\"\n";

            return new TranspiledDeviceMethodResult(
                methodSymbol,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name,
                null,
                "", errorSource, "", errorSource, new EquatableArray<string>(), collector.GetDiagnostics());
        }
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node, string attributeName)
    {
        if (node is not MethodDeclarationSyntax { AttributeLists.Count: > 0 } m)
        {
            return false;
        }

        foreach (var attributeList in m.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var simpleName = GetSimpleName(attribute.Name);
                if (simpleName != null &&
                    KnownAttributes.IsAttributeName(simpleName.Identifier.ValueText, attributeName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SimpleNameSyntax? GetSimpleName(NameSyntax name) => name switch
    {
        SimpleNameSyntax simple => simple,
        QualifiedNameSyntax qualified => qualified.Right,
        AliasQualifiedNameSyntax alias => alias.Name,
        _ => null
    };

    private static void Execute(Compilation compilation, ImmutableArray<TranspiledKernelResult> kernelMethods,
        ImmutableArray<TranspiledDeviceMethodResult> deviceMethods, GeneratorConfig config,
        SourceProductionContext context)
    {
        if (kernelMethods.IsDefaultOrEmpty)
        {
            return;
        }

        var isCuda = config.TargetDialect.Equals("Cuda", StringComparison.OrdinalIgnoreCase);
        ITranspilerDialect dialect = isCuda ? new GSharpCudaDialect() : new StandardCppDialect();

        var distinctDeviceMethods = ImmutableArray.CreateBuilder<TranspiledDeviceMethodResult>();
        var seenDeviceMethods = new HashSet<TranspiledDeviceMethodResult>();
        foreach (var m in deviceMethods)
        {
            if (seenDeviceMethods.Add(m))
            {
                distinctDeviceMethods.Add(m);
            }
        }

        var commonDeviceSource =
            GenerateCommonDeviceSource(distinctDeviceMethods, context, isCuda);
        var preludeSource = GetPreludeSource(dialect);

        EmitSharedSources(context, config, preludeSource, commonDeviceSource);

        var allKernelsSource = new StringBuilder();
        var seenKernelMethods = new HashSet<TranspiledKernelResult>();
        var distinctKernelMethods = ImmutableArray.CreateBuilder<TranspiledKernelResult>();

        var methodsByClass =
            new Dictionary<INamedTypeSymbol, ImmutableArray<TranspiledKernelResult>.Builder>(SymbolEqualityComparer
                .Default);
        foreach (var method in kernelMethods)
        {
            if (seenKernelMethods.Add(method))
            {
                distinctKernelMethods.Add(method);
                var source = isCuda ? method.CudaSource : method.CppSource;
                allKernelsSource.AppendLine(source);

                if (!methodsByClass.TryGetValue(method.MethodSymbol.ContainingType, out var builder))
                {
                    builder = ImmutableArray.CreateBuilder<TranspiledKernelResult>();
                    methodsByClass[method.MethodSymbol.ContainingType] = builder;
                }

                builder.Add(method);
            }
        }

        foreach (var kvp in methodsByClass)
        {
            ProcessKernelClass(kvp.Key, kvp.Value, config, context, isCuda);
        }

        if (!isCuda)
        {
            EmitManagedDeviceStateMachines(distinctDeviceMethods, context);
        }

        var assemblyName = compilation.AssemblyName ?? "UnknownAssembly";
        var safeAssemblyName = AotHostNames.ToSafeIdentifier(assemblyName);
        var requiredIncludes = new HashSet<string>();
        foreach (var method in distinctKernelMethods)
        {
            foreach (var inc in method.RequiredIncludes)
            {
                requiredIncludes.Add(inc);
            }
        }
        foreach (var method in distinctDeviceMethods)
        {
            foreach (var inc in method.RequiredIncludes)
            {
                requiredIncludes.Add(inc);
            }
        }

        if (!IsDesignTimeCompilation(compilation))
        {
            WriteUnityAotArtifact(safeAssemblyName, config, preludeSource, commonDeviceSource,
                allKernelsSource.ToString(), dialect, context, requiredIncludes);
        }

        if (!isCuda)
        {
            return;
        }

        var contextSource = WrapperEmitter.GenerateModuleContextClass(assemblyName, distinctKernelMethods);
        context.AddSource($"{AotHostNames.GetModuleContextName(assemblyName)}.g.cs",
            SourceText.From(contextSource, Encoding.UTF8));
    }

    private static void EmitSharedSources(SourceProductionContext context, GeneratorConfig config, string preludeSource,
        string commonDeviceSource)
    {
        if (!config.EmitSource)
        {
            return;
        }

        using var preludeWriter = new SourceWriter();
        preludeWriter.AppendGeneratedFileHeader(enableNullable: false);
        preludeWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (preludeWriter.Scope())
        {
            preludeWriter.AppendGeneratedCodeAttributes();
            preludeWriter.AppendLine($"internal static class GSharpShared{config.TargetDialect}Prelude");
            using (preludeWriter.Scope())
            {
                preludeWriter.Append("public const string Source = @\"");
                preludeWriter.AppendVerbatimLiteral(preludeSource);
                preludeWriter.AppendLine("\";");
            }
        }

        context.AddSource($"GSharpShared{config.TargetDialect}Prelude.g.cs",
            SourceText.From(preludeWriter.ToString(), Encoding.UTF8));

        using var deviceWriter = new SourceWriter();
        deviceWriter.AppendGeneratedFileHeader(enableNullable: false);
        deviceWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (deviceWriter.Scope())
        {
            deviceWriter.AppendGeneratedCodeAttributes();
            deviceWriter.AppendLine($"internal static class GSharpShared{config.TargetDialect}DeviceCode");
            using (deviceWriter.Scope())
            {
                deviceWriter.Append("public const string Source = @\"");
                deviceWriter.AppendVerbatimLiteral(commonDeviceSource);
                deviceWriter.AppendLine("\";");
            }
        }

        context.AddSource($"GSharpShared{config.TargetDialect}DeviceCode.g.cs",
            SourceText.From(deviceWriter.ToString(), Encoding.UTF8));
    }

    // The managed fallback runs the state machines of the device functions that may suspend; they are emitted into the
    // partial class that declares each function.
    private static void EmitManagedDeviceStateMachines(IReadOnlyList<TranspiledDeviceMethodResult> deviceMethods,
        SourceProductionContext context)
    {
        var sourcesByClass = new Dictionary<INamedTypeSymbol, ImmutableArray<string>.Builder>(SymbolEqualityComparer.Default);
        foreach (var method in deviceMethods)
        {
            if (method.ManagedCSharpSource.Length == 0)
            {
                continue;
            }

            var classSymbol = method.MethodSymbol.ContainingType;
            if (!sourcesByClass.TryGetValue(classSymbol, out var sources))
            {
                sources = ImmutableArray.CreateBuilder<string>();
                sourcesByClass[classSymbol] = sources;
            }

            sources.Add(method.ManagedCSharpSource);
        }

        foreach (var kvp in sourcesByClass)
        {
            var hintName = GlobalCppNames.GetHintName(kvp.Key);
            context.AddSource(hintName[..^".g.cs".Length] + ".DeviceStateMachines.g.cs",
                SourceText.From(WrapperEmitter.GenerateDeviceStateMachines(kvp.Key, kvp.Value), Encoding.UTF8));
        }
    }

    private static void ProcessKernelClass(INamedTypeSymbol classSymbol, IEnumerable<TranspiledKernelResult> methods,
        GeneratorConfig config,
        SourceProductionContext context, bool isCuda)
    {
        var methodsData =
            ImmutableArray.CreateBuilder<(IMethodSymbol Method, string Source, string ManagedCSharpSource)>();
        foreach (var method in methods)
        {
            foreach (var diag in method.Diagnostics)
            {
                context.ReportDiagnostic(diag.Diagnostic);
            }

            if (KernelHostNames.TryFindIndistinguishableOverload(method.MethodSymbol, out var earlier, out var wrapper))
            {
                var location = method.MethodSymbol.Locations.Length > 0 ? method.MethodSymbol.Locations[0] : Location.None;
                context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.IndistinguishableKernelOverloads, location,
                    KernelHostNames.FormatSignature(earlier), KernelHostNames.FormatSignature(method.MethodSymbol), wrapper));
            }

            var source = isCuda ? method.CudaSource : method.CppSource;
            methodsData.Add((method.MethodSymbol, source, method.ManagedCSharpSource));
        }

        ReportDuplicateAotContextNames(classSymbol, methodsData, context);

        var wrapperSource = WrapperEmitter.GenerateKernelExtensions(classSymbol, methodsData, config);
        context.AddSource(GlobalCppNames.GetHintName(classSymbol),
            SourceText.From(wrapperSource, Encoding.UTF8));
    }


    // GSH0058: AotContexts drops a trailing "Context", namespaces and type arguments, so different structs can share
    // one name.
    private static void ReportDuplicateAotContextNames(INamedTypeSymbol classSymbol,
        ImmutableArray<(IMethodSymbol Method, string Source, string ManagedCSharpSource)>.Builder methods,
        SourceProductionContext context)
    {
        var duplicates = ImmutableArray.CreateBuilder<(INamedTypeSymbol Kept, INamedTypeSymbol Duplicate, string Name)>();
        StructMarshalingEmitter.CollectUniqueStructs(methods, duplicates);
        foreach (var (kept, duplicate, name) in duplicates)
        {
            var location = duplicate.Locations.Length > 0 ? duplicate.Locations[0] : Location.None;
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.DuplicateAotContextName, location,
                FormatStructName(kept), FormatStructName(duplicate), name, classSymbol.Name));
        }
    }

    private static string FormatStructName(INamedTypeSymbol type)
    {
        using var writer = new SourceWriter();
        writer.AppendFullyQualifiedCsharpName(type);
        return writer.ToString();
    }

    private static bool IsDesignTimeCompilation(Compilation compilation)
    {
        var domainName = AppDomain.CurrentDomain.FriendlyName;
        if ((domainName.IndexOf("Roslyn.Worker", StringComparison.OrdinalIgnoreCase) >= 0 ||
             domainName.IndexOf("JetBrains", StringComparison.OrdinalIgnoreCase) >= 0 ||
             domainName.IndexOf("ServiceHub", StringComparison.OrdinalIgnoreCase) >= 0 ||
             domainName.IndexOf("LanguageServer", StringComparison.OrdinalIgnoreCase) >= 0 ||
             domainName.IndexOf("OmniSharp", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if ((baseDir.IndexOf("resharper", StringComparison.OrdinalIgnoreCase) >= 0 ||
             baseDir.IndexOf("jetbrains", StringComparison.OrdinalIgnoreCase) >= 0 ||
             baseDir.IndexOf("ServiceHub", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return true;
        }

        foreach (var reference in compilation.References)
        {
            if (reference is CompilationReference)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteUnityAotArtifact(string safeAssemblyName, GeneratorConfig config,
        string preludeSource, string commonDeviceSource, string allKernelsSource, ITranspilerDialect dialect,
        SourceProductionContext context, HashSet<string> requiredIncludes)
    {
        var includesBuilder = new StringBuilder();
        foreach (var inc in requiredIncludes)
        {
            includesBuilder.AppendLine($"#include {inc}");
        }
        var bodySource = includesBuilder + preludeSource + commonDeviceSource + allKernelsSource;
        var sourceHash = GSharpNameEncoding.ComputeSourceHash(bodySource);
        var fullSource = GSharpNameEncoding.FormatSymbolHeaderLine(sourceHash) + "\n" + bodySource;
        var dialectName = dialect.IsCuda ? "Cuda" : "Cpu";
        var gsymContent = GSharpNameEncoding.BuildSymbolFileContent(bodySource, dialectName, static id => GlobalCppNames.TryGetFullName(id, out var full) ? full : null);

        var ext = dialect.IsCuda ? ".g.cu" : ".g.cpp";
        var fileName = $"Kassyi.GSharp.Kernels_{safeAssemblyName}{ext}";
        var safeIntermediateOutputPath = config.IntermediateOutputPath ?? "";
        if (string.IsNullOrEmpty(safeIntermediateOutputPath))
        {
            return;
        }

        var filePath = Path.Combine(safeIntermediateOutputPath, fileName);
        var gsymFilePath = filePath + ".gsym.json";

#pragma warning disable RS1035 // Do not do file IO in analyzers
        try
        {
            if (!Directory.Exists(safeIntermediateOutputPath))
            {
                Directory.CreateDirectory(safeIntermediateOutputPath);
            }

            File.WriteAllText(filePath, fullSource);
            File.WriteAllText(gsymFilePath, gsymContent);
        }
        catch (Exception ex)
        {
            // Ignore IO errors on best-effort basis, but report them as an info diagnostic
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.AotArtifactWriteFailed,
                Location.None,
                filePath,
                ex.Message);
            context.ReportDiagnostic(diagnostic);
        }
#pragma warning restore RS1035
    }

    private static string GenerateCommonDeviceSource(IReadOnlyList<TranspiledDeviceMethodResult> deviceMethods,
        SourceProductionContext context, bool isCuda)
    {
        var deviceSourceBuilder = new StringBuilder();

        // Pass 1: Forward declarations
        foreach (var method in deviceMethods)
        {
            foreach (var diag in method.Diagnostics)
            {
                context.ReportDiagnostic(diag.Diagnostic);
            }

            deviceSourceBuilder.AppendLine(isCuda ? method.CudaSignature : method.CppSignature);
        }

        // Pass 2: Method bodies. A frame embeds the frames of its calls by value, so callees come first (CPU dialects).
        var methodsByFrame = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < deviceMethods.Count; i++)
        {
            if (deviceMethods[i].FrameName.Length > 0)
            {
                methodsByFrame[deviceMethods[i].FrameName] = i;
            }
        }

        var emitted = new bool[deviceMethods.Count];
        for (var i = 0; i < deviceMethods.Count; i++)
        {
            AppendBody(i);
        }

        return deviceSourceBuilder.ToString();

        void AppendBody(int index)
        {
            if (emitted[index])
            {
                return;
            }

            emitted[index] = true;
            var method = deviceMethods[index];
            if (!isCuda)
            {
                foreach (var dependency in method.FrameDependencies)
                {
                    if (methodsByFrame.TryGetValue(dependency, out var dependencyIndex))
                    {
                        AppendBody(dependencyIndex);
                    }
                }
            }

            deviceSourceBuilder.AppendLine(isCuda ? method.CudaBody : method.CppBody);
        }
    }

    private static string GetPreludeSource(ITranspilerDialect dialect) =>
        dialect.IsCuda ? GSharpPreludeResources.Cuda : GSharpPreludeResources.Cpp;

    private static ImmutableArray<string> CombineIncludes(HashSet<string> a, HashSet<string> b)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var item in a)
        {
            builder.Add(item);
        }
        foreach (var item in b)
        {
            if (a.Contains(item))
            {
                continue;
            }

            builder.Add(item);
        }
        return builder.ToImmutable();
    }
}
