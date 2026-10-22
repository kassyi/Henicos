namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Build-time generator for the built-in native API definitions under <c>src/NativeApi</c>.
/// </summary>
/// <remarks>
/// Shares <see cref="NativeApiYamlReader"/> and <see cref="NativeApiEmitter"/> with the <c>NativeApiGenerator</c>
/// source generator so that built-in and user-supplied definitions are parsed and emitted identically.
/// </remarks>
public static class YamlApiGenerator
{
    public static void Run(string inputDir, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var definitions = ImmutableArray.CreateBuilder<NativeApiDefinition>();
        var paths = Directory.GetFiles(inputDir, "*.*", SearchOption.AllDirectories);
        Array.Sort(paths, StringComparer.Ordinal);

        foreach (var path in paths)
        {
            if (!path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!NativeApiYamlReader.TryRead(File.ReadAllText(path), out var definition, out var error))
            {
                // MSBuild canonical error format so that the failure surfaces in the build output and IDE.
                throw new InvalidDataException(
                    $"{path}({error.Line}): error {error.DiagnosticId}: {error.Message}");
            }

            definitions.Add(definition);
        }

        var mappingEntries = ImmutableArray.CreateBuilder<KeyValuePair<string, string>>();

        foreach (var def in definitions)
        {
            string source;
            string fileName;
            switch (def.BindingKind)
            {
                case NativeApiBindingKind.Library:
                    source = NativeApiEmitter.EmitLibraryBindings(def);
                    fileName = $"{def.ClassName}.g.cs";
                    break;
                case NativeApiBindingKind.Module:
                    source = NativeApiEmitter.EmitModuleBindings(def);
                    fileName = $"{def.ClassName}.g.cs";
                    break;
                default:
                    source = NativeApiEmitter.EmitIntrinsics(def, mappingEntries);
                    fileName = $"{def.ClassName}Intrinsics.g.cs";
                    break;
            }

            File.WriteAllText(Path.Combine(outputDir, fileName), source, Encoding.UTF8);
        }

        // Generate mapping info for compiler
        var mappingSource = NativeApiEmitter.EmitMappingInfo(mappingEntries);
        File.WriteAllText(Path.Combine(outputDir, "NativeApiMapper.g.cs"), mappingSource, Encoding.UTF8);
    }
}
