namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>
/// Reads <c>diagnostics.yml</c>: a YAML list with one mapping per diagnostic (the keys are described at the top of the
/// file). A key the file does not know, a value of the wrong kind or a repeated key is an error with its line.
/// </summary>
public static class DiagnosticCatalogReader
{
    /// <summary>Reads the diagnostic definitions of <paramref name="path"/>, in file order.</summary>
    /// <exception cref="GeneratorInputException">The file is not a list of diagnostic mappings.</exception>
    public static List<DiagnosticDefinition> Read(string path)
    {
        YamlNode root;
        try
        {
            root = YamlSubsetParser.Parse(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (NativeApiReadException ex)
        {
            throw new GeneratorInputException(path, ex.ToError().Line, ex.Message);
        }

        if (root is not YamlSequence list)
        {
            throw new GeneratorInputException(path, root.Line, "The file must hold a YAML list of diagnostics.");
        }

        var definitions = new List<DiagnosticDefinition>(list.Items.Count);
        foreach (var item in list.Items)
        {
            if (item is not YamlMapping entry)
            {
                throw new GeneratorInputException(path, item.Line, "Each diagnostic must be a mapping.");
            }

            definitions.Add(ReadEntry(path, entry));
        }

        return definitions;
    }

    private static DiagnosticDefinition ReadEntry(string path, YamlMapping entry)
    {
        var definition = new DiagnosticDefinition();
        foreach (var (key, node) in entry.Entries)
        {
            if (node is not YamlScalar { IsNull: false } scalar)
            {
                throw new GeneratorInputException(path, node.Line, $"The value of '{key}' must be a scalar.");
            }

            var value = scalar.Value;
            switch (key)
            {
                case "name":
                    definition.Name = value;
                    break;
                case "id":
                    definition.Id = value;
                    break;
                case "title":
                    definition.Title = value;
                    break;
                case "messageFormat":
                    definition.MessageFormat = value;
                    break;
                case "category":
                    definition.Category = value;
                    break;
                case "description":
                    definition.Description = value;
                    break;
                case "defaultSeverity":
                    definition.DefaultSeverity = value;
                    break;
                case "isEnabledByDefault":
                    definition.IsEnabledByDefault = ReadBoolean(path, key, scalar);
                    break;
                case "isObsolete":
                    definition.IsObsolete = ReadBoolean(path, key, scalar);
                    break;
                default:
                    throw new GeneratorInputException(path, node.Line, $"Unknown key '{key}'.");
            }
        }

        return definition;
    }

    private static bool ReadBoolean(string path, string key, YamlScalar scalar) => scalar.Value switch
    {
        "true" => true,
        "false" => false,
        _ => throw new GeneratorInputException(
            path, scalar.Line, $"The value of '{key}' must be true or false, not '{scalar.Value}'."),
    };
}
