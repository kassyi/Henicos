namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Generates native bindings from YAML definitions supplied as
/// <c>&lt;AdditionalFiles Include="*.yml" GSharpApi="true" /&gt;</c>.
/// </summary>
/// <remarks>
/// <para>A definition with <c>libraryName</c> produces <c>[DllImport]</c> bindings; one with <c>moduleName</c>
/// produces launchers for kernels inside a fatbin / cubin / PTX module. Libraries that need probing
/// (platform candidates or relative paths) are routed through a single generated <c>DllImportResolver</c>.</para>
/// <para>All definitions are processed together so that duplicate classes can be reported instead of failing on a
/// hint-name collision; the step is cached on the file paths and contents only.</para>
/// </remarks>
[Generator]
public sealed class NativeApiGenerator : IIncrementalGenerator
{
    public const string GSharpApiMetadataKey = "build_metadata.AdditionalFiles.GSharpApi";

    public const string TrackingName = "NativeApiFiles";

    public void Initialize(IncrementalGeneratorInitializationContext context) =>
        context.RegisterSourceOutput(GetSourceFiles(context).WithTrackingName(TrackingName), Emit);

    /// <summary>
    /// The native API definition files of the compilation (<c>GSharpApi="true"</c>), in the build's order.
    /// </summary>
    internal static IncrementalValueProvider<ImmutableArray<NativeApiSourceFile>> GetSourceFiles(
        IncrementalGeneratorInitializationContext context) =>
        context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair => IsGSharpApi(pair.Right.GetOptions(pair.Left)))
            .Select(static (pair, cancellationToken) =>
                new NativeApiSourceFile(pair.Left.Path, pair.Left.GetText(cancellationToken)?.ToString() ?? string.Empty))
            .Collect();

    /// <summary>
    /// The classes that the module bindings of <paramref name="files"/> extend, each with the module whose field the
    /// binding declares, for the <c>[CudaImport]</c> methods of the same class and module.
    /// </summary>
    internal static EquatableArray<YamlModuleClass> GetModuleClasses(ImmutableArray<NativeApiSourceFile> files,
        CancellationToken cancellationToken)
    {
        var classes = ImmutableArray.CreateBuilder<YamlModuleClass>();
        foreach (var result in ReadDefinitions(files, cancellationToken))
        {
            if (result.Definition is { BindingKind: NativeApiBindingKind.Module } definition)
            {
                classes.Add(new YamlModuleClass($"{definition.Namespace}.{definition.ClassName}",
                    definition.ModuleName ?? string.Empty));
            }
        }

        return classes.ToImmutable().AsEquatableArray();
    }

    private static bool IsGSharpApi(AnalyzerConfigOptions options) =>
        options.TryGetValue(GSharpApiMetadataKey, out var value) &&
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static void Emit(SourceProductionContext context, ImmutableArray<NativeApiSourceFile> files)
    {
        var resolverDefinitions = ImmutableArray.CreateBuilder<(string Path, NativeApiDefinition Definition)>();
        foreach (var result in ReadDefinitions(files, context.CancellationToken))
        {
            if (result.Definition is not { } definition)
            {
                Report(context, result.File, result.Error ?? throw new InvalidOperationException("No definition."));
                continue;
            }

            var source = definition.BindingKind == NativeApiBindingKind.Library
                ? NativeBindingEmitter.EmitLibraryBindings(definition)
                : NativeBindingEmitter.EmitModuleBindings(definition);
            context.AddSource($"{definition.Namespace}.{definition.ClassName}.NativeApi.g.cs", source);
            resolverDefinitions.Add((result.File.Path, definition));
        }

        var resolver = NativeLibraryResolverPlanner.Plan(resolverDefinitions.ToImmutable());
        if (!resolver.IsEmpty)
        {
            context.AddSource($"{NativeBindingEmitter.ResolverClassName}.g.cs",
                NativeBindingEmitter.EmitLibraryResolver(resolver));
        }
    }

    // Each file read: the definition to emit, or the error that keeps it out (a YAML or schema error, an intrinsic
    // definition, a class that an earlier file already defines)
    private static ImmutableArray<NativeApiReadResult> ReadDefinitions(ImmutableArray<NativeApiSourceFile> files,
        CancellationToken cancellationToken)
    {
        var results = ImmutableArray.CreateBuilder<NativeApiReadResult>(files.Length);
        var emittedClasses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(Read(file, emittedClasses));
        }

        return results.MoveToImmutable();
    }

    private static NativeApiReadResult Read(NativeApiSourceFile file, HashSet<string> emittedClasses)
    {
        if (!NativeApiYamlReader.TryRead(file.Text, out var definition, out var error))
        {
            return new NativeApiReadResult(file, null, error);
        }

        if (definition.BindingKind == NativeApiBindingKind.Intrinsic)
        {
            return new NativeApiReadResult(file, null, new NativeApiReadError(NativeApiReadErrorKind.Schema, 1,
                "Either 'libraryName' (host library) or 'moduleName' (kernel module) must be specified."));
        }

        var qualifiedName = $"{definition.Namespace}.{definition.ClassName}";
        if (emittedClasses.Add(qualifiedName))
        {
            return new NativeApiReadResult(file, definition, null);
        }

        return new NativeApiReadResult(file, null, new NativeApiReadError(NativeApiReadErrorKind.Schema,
            definition.ClassNameLine, $"Class '{qualifiedName}' is already defined by another native API definition."));
    }

    private static void Report(SourceProductionContext context, NativeApiSourceFile file, NativeApiReadError error)
    {
        var descriptor = error.Kind == NativeApiReadErrorKind.Syntax
            ? GSharpDiagnostics.NativeApiYamlSyntaxError
            : GSharpDiagnostics.NativeApiSchemaViolation;

        var position = new LinePosition(Math.Max(0, error.Line - 1), 0);
        var location = Location.Create(file.Path, default, new LinePositionSpan(position, position));
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, Path.GetFileName(file.Path), error.Message));
    }

    // A file and its definition, or the error that keeps it from being emitted
    private readonly record struct NativeApiReadResult(
        NativeApiSourceFile File,
        NativeApiDefinition? Definition,
        NativeApiReadError? Error);
}
