using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>Roslyn incremental source generator that transpiles C# GPU kernels to CUDA C++ and emits host invocations.</summary>
[Generator]
public class GpuKernelGenerator : IIncrementalGenerator
{
    /// <summary>The tracking name of the step that transpiles each <c>[GpuKernel]</c>, for incremental-caching tests.</summary>
    public const string KernelTrackingName = "GpuKernelMethod";

    /// <summary>The tracking name of the step that transpiles each <c>[GpuDeviceFunction]</c>.</summary>
    public const string DeviceFunctionTrackingName = "GpuDeviceFunction";

    /// <summary>
    /// Collects what one dialect's transpilation could not emit (<see cref="TranspileFailure"/>), each reason once, and
    /// states the reasons in place of that dialect's output.
    /// </summary>
    private sealed class FailureCollector
    {
        private readonly ImmutableArray<string>.Builder _failures = ImmutableArray.CreateBuilder<string>();

        // The text is what the generated source states, so a failure found by both the signature and the body is one
        public void Fail(TranspileFailure failure)
        {
            var reason = failure.Format();
            if (!_failures.Contains(reason))
            {
                _failures.Add(reason);
            }
        }

        /// <summary>
        /// <paramref name="source"/> when nothing failed; otherwise one <c>#error</c> per reason, so that a failure the
        /// analyzer misses still stops the native build. The analyzer reports the same failures as diagnostics.
        /// </summary>
        public string Apply(string source) =>
            _failures.Count == 0 ? source : NativeModuleAssembler.FormatTranspileFailures(_failures);
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
                var dialect = TranspilerDialects.GetName(options.GlobalOptions);

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

                return new GeneratorConfig(dialect, emitSource, intermediatePath);
            });

        var kernelMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuKernel),
                transform: static (ctx, _) => TranspileKernel(ctx, KnownAttributes.GpuKernel))
            .WithTrackingName(KernelTrackingName)
            .Where(static m => m.HasValue)
            .Select(static (m, _) => m.GetValueOrDefault());

        var deviceMethods = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsSyntaxTargetForGeneration(s, KnownAttributes.GpuDeviceFunction),
                transform: static (ctx, _) => TranspileDeviceMethod(ctx, KnownAttributes.GpuDeviceFunction))
            .WithTrackingName(DeviceFunctionTrackingName)
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
            var cudaFailures = new FailureCollector();
            var cppFailures = new FailureCollector();
            var cudaDialect = new GSharpCudaDialect();
            var cppDialect = new StandardCppDialect();

            var plan = MethodEmissionPlan.Create(methodDeclaration, context.SemanticModel, isKernel: true);

            var cudaSource = cudaFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cudaFailures.Fail, cudaDialect,
                out var requiredIncludesCuda));
            var cppSource = cppFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cppFailures.Fail, cppDialect,
                out var requiredIncludesCpp));
            var managedCSharpSource = MethodTranspiler.GenerateManagedCSharpSource(in plan);

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
                    ToIncludes(requiredIncludesCuda),
                    ToIncludes(requiredIncludesCpp),
                    ""
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
                ToIncludes(requiredIncludesCuda),
                ToIncludes(requiredIncludesCpp),
                ""
            );
        }
        catch (Exception ex)
        {
            return new TranspiledKernelResult(
                methodSymbol,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name,
                methodSymbol.ContainingNamespace?.GetFullNamespaceName(),
                "", "", $"/*\n{ex}\n*/\n#error \"Internal Compiler Error: Check comments for details\"\n",
                new EquatableArray<string>(), new EquatableArray<string>(), ex.ToString());
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
            var cudaFailures = new FailureCollector();
            var cppFailures = new FailureCollector();
            var cudaDialect = new GSharpCudaDialect();
            var cppDialect = new StandardCppDialect();

            var plan = MethodEmissionPlan.Create(methodDeclaration, context.SemanticModel, isKernel: false);

            var cudaSignature = MethodTranspiler.GenerateCudaSignature(in plan, cudaFailures.Fail, cudaDialect);
            var cppSignature = MethodTranspiler.GenerateCudaSignature(in plan, cppFailures.Fail, cppDialect);

            var cudaBody = cudaFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cudaFailures.Fail, cudaDialect,
                out var requiredIncludesCuda));
            var cppBody = cppFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan, cppFailures.Fail, cppDialect,
                out var requiredIncludesCpp));

            // A device function that may suspend has a frame, which embeds the frames of the calls it makes
            var frameName = "";
            var frameDependencies = ImmutableArray.CreateBuilder<string>();
            var managedCSharpSource = "";
            if (plan.Kind == MethodEmissionKind.SuspendingDeviceFunction && methodDeclaration.GetBodyNode() is { } body)
            {
                frameName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, methodSymbol);
                foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body,
                             context.SemanticModel.Compilation))
                {
                    frameDependencies.Add(GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, callee));
                }

                managedCSharpSource = MethodTranspiler.GenerateManagedDeviceFunctionSource(in plan);
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
                ToIncludes(requiredIncludesCuda),
                ToIncludes(requiredIncludesCpp),
                "",
                frameName,
                new EquatableArray<string>(frameDependencies.ToImmutable()),
                managedCSharpSource);
        }
        catch (Exception ex)
        {
            var errorSource = $"/*\n{ex}\n*/\n#error \"Internal Compiler Error: Check comments for details\"\n";

            return new TranspiledDeviceMethodResult(
                methodSymbol,
                methodSymbol.Name,
                methodSymbol.ContainingType.Name,
                null,
                "", errorSource, "", errorSource, new EquatableArray<string>(), new EquatableArray<string>(), ex.ToString());
        }
    }

    private static bool IsSyntaxTargetForGeneration(SyntaxNode node, string attributeName)
    {
        if (node is not MethodDeclarationSyntax { AttributeLists.Count: > 0 } m)
        {
            return false;
        }

        return m.AttributeLists
            .AsStackEnumerable()
            .SelectMany(static list => list.Attributes
                .AsStackEnumerable()
            ).Any(
                attributeName,
                static (attribute, name) =>
                    GetSimpleName(attribute.Name) is { } simpleName &&
                    KnownAttributes.IsAttributeName(simpleName.Identifier.ValueText, name)
            );
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

        var isCuda = config.IsCuda;
        var dialect = config.CreateDialect();

        var distinctDeviceMethods = ImmutableArray.CreateBuilder<TranspiledDeviceMethodResult>();
        deviceMethods
            .AsStackEnumerable()
            .AddIf(distinctDeviceMethods, new HashSet<TranspiledDeviceMethodResult>(), static (m, seen) => seen.Add(m));

        foreach (var method in distinctDeviceMethods)
        {
            ReportInternalError(context, method.InternalError);
        }

        var commonDeviceSource = NativeModuleAssembler.AssembleDeviceSource(distinctDeviceMethods, isCuda);
        var preludeSource = NativeModuleAssembler.GetPreludeSource(dialect);

        EmitSharedSources(context, config, preludeSource, commonDeviceSource);

        var allKernelsSource = new StringBuilder();
        var seenKernelMethods = new HashSet<TranspiledKernelResult>();
        var distinctKernelMethods = ImmutableArray.CreateBuilder<TranspiledKernelResult>();

        // Keyed by the hint name, not the class symbol: a cached result keeps the symbol of an earlier compilation, which
        // never equals the same class in the current one (#472)
        var methodsByClass =
            new Dictionary<string, (INamedTypeSymbol Class, ImmutableArray<TranspiledKernelResult>.Builder Methods)>(
                StringComparer.Ordinal);
        foreach (var method in kernelMethods)
        {
            if (seenKernelMethods.Add(method))
            {
                distinctKernelMethods.Add(method);
                var source = method.GetSource(isCuda);
                allKernelsSource.AppendLine(source);

                var classSymbol = method.MethodSymbol.ContainingType;
                var hintName = GlobalCppNames.GetHintName(classSymbol);
                if (!methodsByClass.TryGetValue(hintName, out var group))
                {
                    group = (classSymbol, ImmutableArray.CreateBuilder<TranspiledKernelResult>());
                    methodsByClass[hintName] = group;
                }

                group.Methods.Add(method);
            }
        }

        methodsByClass
            .AsStackEnumerable()
            .Do(
                (Config: config, Context: context, IsCuda: isCuda),
                static (kvp, s) => ProcessKernelClass(kvp.Value.Class, kvp.Value.Methods, s.Config, s.Context, s.IsCuda)
            );

        if (!isCuda)
        {
            EmitManagedDeviceStateMachines(distinctDeviceMethods, context);
        }

        var requiredIncludes = NativeModuleAssembler.CollectIncludes(distinctKernelMethods, distinctDeviceMethods, isCuda);
        var bodySource = NativeModuleAssembler.AssembleBody(requiredIncludes, preludeSource, commonDeviceSource,
            allKernelsSource.ToString());
        AotArtifactWriter.Write(compilation, config, bodySource, context);

        if (!isCuda)
        {
            return;
        }

        var assemblyName = compilation.AssemblyName ?? "UnknownAssembly";
        var contextSource = CudaHostLaunchEmitter.GenerateModuleContextClass(assemblyName, distinctKernelMethods);
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
            preludeWriter.AppendLine($"internal static class {config.SharedPreludeClassName}");
            using (preludeWriter.Scope())
            {
                preludeWriter.Append("public const string Source = @\"");
                preludeWriter.AppendVerbatimLiteral(preludeSource);
                preludeWriter.AppendLine("\";");
            }
        }

        context.AddSource($"{config.SharedPreludeClassName}.g.cs",
            SourceText.From(preludeWriter.ToString(), Encoding.UTF8));

        using var deviceWriter = new SourceWriter();
        deviceWriter.AppendGeneratedFileHeader(enableNullable: false);
        deviceWriter.AppendLine("namespace Kassyi.GSharp.Generated");
        using (deviceWriter.Scope())
        {
            deviceWriter.AppendGeneratedCodeAttributes();
            deviceWriter.AppendLine($"internal static class {config.SharedDeviceCodeClassName}");
            using (deviceWriter.Scope())
            {
                deviceWriter.Append("public const string Source = @\"");
                deviceWriter.AppendVerbatimLiteral(commonDeviceSource);
                deviceWriter.AppendLine("\";");
            }
        }

        context.AddSource($"{config.SharedDeviceCodeClassName}.g.cs",
            SourceText.From(deviceWriter.ToString(), Encoding.UTF8));
    }

    // The managed fallback runs the state machines of the device functions that may suspend; they are emitted into the
    // partial class that declares each function.
    private static void EmitManagedDeviceStateMachines(IReadOnlyList<TranspiledDeviceMethodResult> deviceMethods,
        SourceProductionContext context)
    {
        // Keyed by the hint name for the same reason as the kernels (#472)
        var sourcesByClass =
            new Dictionary<string, (INamedTypeSymbol Class, ImmutableArray<string>.Builder Sources)>(StringComparer.Ordinal);
        foreach (var method in deviceMethods)
        {
            if (method.ManagedCSharpSource.Length == 0)
            {
                continue;
            }

            var classSymbol = method.MethodSymbol.ContainingType;
            var hintName = GlobalCppNames.GetHintName(classSymbol);
            if (!sourcesByClass.TryGetValue(hintName, out var group))
            {
                group = (classSymbol, ImmutableArray.CreateBuilder<string>());
                sourcesByClass[hintName] = group;
            }

            group.Sources.Add(method.ManagedCSharpSource);
        }

        foreach (var kvp in sourcesByClass)
        {
            context.AddSource(kvp.Key[..^".g.cs".Length] + ".DeviceStateMachines.g.cs",
                SourceText.From(WrapperEmitter.GenerateDeviceStateMachines(kvp.Value.Class, kvp.Value.Sources),
                    Encoding.UTF8));
        }
    }

    // The generator reports only its own failure (GSH0048); the analyzers report the user's errors, and a construct the
    // transpiler could not emit is stated in the output as #error
    private static void ReportInternalError(SourceProductionContext context, string internalError)
    {
        if (internalError.Length > 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(GSharpDiagnostics.InternalCompilerError, Location.None, internalError));
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
            ReportInternalError(context, method.InternalError);

            var source = method.GetSource(isCuda);
            methodsData.Add((method.MethodSymbol, source, method.ManagedCSharpSource));
        }

        // KernelClassAnalyzer reports the overloads and structs that the wrappers leave out (GSH0059, GSH0058)
        var wrapperSource = WrapperEmitter.GenerateKernelExtensions(classSymbol, methodsData, config);
        context.AddSource(GlobalCppNames.GetHintName(classSymbol),
            SourceText.From(wrapperSource, Encoding.UTF8));
    }


    private static EquatableArray<string> ToIncludes(HashSet<string> includes)
    {
        var builder = ImmutableArray.CreateBuilder<string>(includes.Count);
        includes
            .AsStackEnumerable()
            .AddTo(builder);
        return builder.MoveToImmutable().AsEquatableArray();
    }
}
