using Kassyi.GSharp.Compiler.Models;

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
        var suppressGs0001 = false;
        var root = methodDeclaration.SyntaxTree.GetRoot();
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
            var cppSource = MethodTranspiler.GenerateCudaSource(methodDeclaration, context.SemanticModel, _ =>
            {
            }, isDevice: false, cppDialect, out var requiredIncludesCpp);

            var managedCSharpSource =
                MethodTranspiler.GenerateManagedCSharpSource(methodDeclaration, context.SemanticModel);

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
#pragma warning disable RS1035
            File.AppendAllText(@"P:\Projects\Active\Henicos\tmp\gsharp_error.txt", ex.ToString() + "\n\n");
#pragma warning restore RS1035
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
                collector.GetDiagnostics());
        }
        catch (Exception ex)
        {
            var collector = new DiagnosticCollector();
#pragma warning disable RS1035
            File.AppendAllText(@"P:\Projects\Active\Henicos\tmp\gsharp_error.txt", ex.ToString() + "\n\n");
#pragma warning restore RS1035
            collector.Report(Diagnostic.Create(
                GSharpDiagnostics.InternalCompilerError,
                Location.None,
                ex.Message));

            return new TranspiledDeviceMethodResult(
                methodSymbol,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name,
                null,
                "", "", "", "", new EquatableArray<string>(), collector.GetDiagnostics());
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
        var preludeSource = GetPreludeSource(context, dialect);

        if (preludeSource == null)
        {
            return;
        }

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

        var assemblyName = compilation.AssemblyName ?? "UnknownAssembly";
        var safeAssemblyName = IdentifierHelper.ToSafeIdentifier(assemblyName);
        var requiredIncludes = new HashSet<string>();
        foreach (var method in distinctKernelMethods)
        {
            if (method.RequiredIncludes != null)
            {
                foreach (var inc in method.RequiredIncludes)
                {
                    requiredIncludes.Add(inc);
                }
            }
        }
        foreach (var method in distinctDeviceMethods)
        {
            foreach (var inc in method.RequiredIncludes)
            {
                requiredIncludes.Add(inc);
            }
        }

        WriteUnityAotArtifact(safeAssemblyName, config, preludeSource, commonDeviceSource, allKernelsSource.ToString(),
            dialect, context, requiredIncludes);

        if (!isCuda)
        {
            return;
        }

        var contextSource = WrapperEmitter.GenerateModuleContextClass(assemblyName, distinctKernelMethods);
        context.AddSource($"GSharpModuleContext_{safeAssemblyName}.g.cs",
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
        preludeWriter.AppendLine("// <auto-generated/>");
        preludeWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (preludeWriter.Scope())
        {
            preludeWriter.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Kassyi.GSharp.Compiler\", \"1.0.0.0\")]");
            preludeWriter.AppendLine("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
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
        deviceWriter.AppendLine("// <auto-generated/>");
        deviceWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (deviceWriter.Scope())
        {
            deviceWriter.AppendLine("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"Kassyi.GSharp.Compiler\", \"1.0.0.0\")]");
            deviceWriter.AppendLine("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
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

            var source = isCuda ? method.CudaSource : method.CppSource;
            methodsData.Add((method.MethodSymbol, source, method.ManagedCSharpSource));
        }

        var wrapperSource = WrapperEmitter.GenerateKernelExtensions(classSymbol, methodsData, config);
        context.AddSource(classSymbol.Name + ".g.cs",
            SourceText.From(wrapperSource, Encoding.UTF8));
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
        var fullSource = includesBuilder + preludeSource + commonDeviceSource + allKernelsSource;
        var ext = dialect.IsCuda ? ".g.cu" : ".g.cpp";
        var fileName = $"Kassyi.GSharp.Kernels_{safeAssemblyName}{ext}";
        var safeIntermediateOutputPath = config.IntermediateOutputPath ?? "";
        if (string.IsNullOrEmpty(safeIntermediateOutputPath))
        {
            return;
        }

        var filePath = Path.Combine(safeIntermediateOutputPath, fileName);

#pragma warning disable RS1035 // Do not do file IO in analyzers
        try
        {
            if (!Directory.Exists(safeIntermediateOutputPath))
            {
                Directory.CreateDirectory(safeIntermediateOutputPath);
            }

            File.WriteAllText(filePath, fullSource);
        }
        catch (Exception ex)
        {
            // Ignore IO errors on best-effort basis, but report them as an info diagnostic
            var diagnosticDescriptor = new DiagnosticDescriptor(
                "GSH0000",
                "AOT Artifact Error",
                "Failed to write AOT artifact to '{0}': {1}",
                "Build",
                DiagnosticSeverity.Info,
                isEnabledByDefault: true);

            var diagnostic = Diagnostic.Create(
                diagnosticDescriptor,
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

        // Pass 2: Method bodies
        foreach (var method in deviceMethods)
        {
            deviceSourceBuilder.AppendLine(isCuda ? method.CudaBody : method.CppBody);
        }

        return deviceSourceBuilder.ToString();
    }

    private static string? GetPreludeSource(SourceProductionContext context, ITranspilerDialect dialect)
    {
        var resourceFileName = dialect.IsCuda ? "Prelude.cu" : "Prelude.cpp";
        var assembly = typeof(GpuKernelGenerator).Assembly;
        string? resourceName = null;
        var resourceCount = 0;
        foreach (var n in assembly.GetManifestResourceNames())
        {
            if (n.EndsWith(resourceFileName, StringComparison.Ordinal))
            {
                resourceName = n;
                resourceCount++;
            }
        }

        if (resourceCount != 1 || resourceName == null)
        {
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.PreludeMissing,
                Location.None,
                assembly.FullName,
                resourceCount);
            context.ReportDiagnostic(diagnostic);
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            var diagnostic = Diagnostic.Create(
                GSharpDiagnostics.PreludeMissing,
                Location.None,
                assembly.FullName,
                0);
            context.ReportDiagnostic(diagnostic);
            return null;
        }

        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd() + "\n";

        return ResolveEmbeddedIncludes(assembly, content, dialect.IsCuda);
    }

    private static string ResolveEmbeddedIncludes(System.Reflection.Assembly assembly, string content, bool isCuda)
    {
        const string IncludePrefix = "#include \"";
        var manifestNames = assembly.GetManifestResourceNames();
        var searchStart = 0;

        while (true)
        {
            var includeIndex = content.IndexOf(IncludePrefix, searchStart, StringComparison.Ordinal);
            if (includeIndex < 0)
            {
                break;
            }

            var pathStart = includeIndex + IncludePrefix.Length;
            var pathEnd = content.IndexOf('"', pathStart);
            if (pathEnd < 0)
            {
                break;
            }

            var includePath = content.AsSpan(pathStart, pathEnd - pathStart);
            string? matchedResource = null;
            foreach (var name in manifestNames)
            {
                if (EndsWithResourcePath(name, includePath))
                {
                    matchedResource = name;
                    break;
                }
            }

            if (matchedResource == null)
            {
                searchStart = pathEnd + 1;
                continue;
            }

            using var includeStream = assembly.GetManifestResourceStream(matchedResource);
            if (includeStream == null)
            {
                searchStart = pathEnd + 1;
                continue;
            }

            using var includeReader = new StreamReader(includeStream);
            var includedContent = includeReader.ReadToEnd();
            var before = content[..includeIndex];
            var after = content[(pathEnd + 1)..];
            if (!isCuda && after.AsSpan().Trim().IsEmpty)
            {
                includedContent = TrimOneTrailingNewline(includedContent);
            }

            content = before + includedContent + after;
            searchStart = includeIndex;
        }

        return content;
    }

    private static bool EndsWithResourcePath(string manifestName, ReadOnlySpan<char> includePath)
    {
        if (manifestName.Length <= includePath.Length)
        {
            return false;
        }

        var offset = manifestName.Length - includePath.Length;
        if (manifestName[offset - 1] != '.')
        {
            return false;
        }

        for (var i = 0; i < includePath.Length; i++)
        {
            var expected = includePath[i] is '/' or '\\' ? '.' : includePath[i];
            if (manifestName[offset + i] != expected)
            {
                return false;
            }
        }

        return true;
    }

    private static string TrimOneTrailingNewline(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            return text[..^2];
        }

        if (text.EndsWith("\n", StringComparison.Ordinal))
        {
            return text[..^1];
        }

        return text;
    }

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
