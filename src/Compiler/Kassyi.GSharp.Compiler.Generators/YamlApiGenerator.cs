namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Build-time generator for the built-in native API definitions under <c>src/NativeApi</c>.
/// </summary>
/// <remarks>
/// Emits only <see cref="NativeApiBindingKind.Intrinsic"/> definitions (into <c>Kassyi.GSharp.Core</c>). Library and
/// module definitions are emitted by the <c>NativeApiGenerator</c> source generator into the consuming assembly; here
/// they are parsed with the shared <see cref="NativeApiYamlReader"/> only, so that broken YAML still fails the build.
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

        foreach (var def in definitions)
        {
            if (def.BindingKind != NativeApiBindingKind.Intrinsic)
            {
                continue;
            }

            var source = NativeIntrinsicEmitter.EmitIntrinsics(def);
            File.WriteAllText(Path.Combine(outputDir, $"{def.ClassName}Intrinsics.g.cs"), source, Encoding.UTF8);
        }
    }
}
