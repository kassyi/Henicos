namespace Kassyi.GSharp.Compiler.Generators;

/// <summary>Generates C# dialect classes implementing ITranspilerDialect from YAML configuration files.</summary>
public static class YamlDialectGenerator
{
    /// <summary>
    /// Discovers YAML dialect files in <paramref name="inputDir"/> and generates dialect C# classes in
    /// <paramref name="outputDir"/>. A <c>*Dialect.g.cs</c> file in <paramref name="outputDir"/> that no dialect wrote
    /// this time is deleted, so a dialect removed from the input is not compiled from an earlier build.
    /// </summary>
    /// <exception cref="GeneratorInputException">A dialect file cannot be read as a dialect.</exception>
    public static void Run(string inputDir, string outputDir)
    {
        if (!Directory.Exists(inputDir))
        {
            throw new GeneratorInputException(inputDir, 0, "The dialect YAML directory does not exist.");
        }

        var paths = Directory.GetFiles(inputDir, "*.*", SearchOption.AllDirectories);
        Array.Sort(paths, StringComparer.Ordinal);

        var dialectDefinitions = new List<DialectDefinition>();
        var defMap = new Dictionary<string, DialectDefinition>(StringComparer.Ordinal);
        // Names that differ only in case would write the same file on a case-insensitive file system.
        var byFileName = new Dictionary<string, DialectDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var def = DialectDefinition.Parse(File.ReadAllText(path), path);
            if (byFileName.TryGetValue(def.Name, out var other))
            {
                throw new GeneratorInputException(path, def.Line, $"The dialect '{def.Name}' is also defined in {other.Path} ('{other.Name}').");
            }

            dialectDefinitions.Add(def);
            defMap.Add(def.Name, def);
            byFileName.Add(def.Name, def);
        }

        foreach (var def in dialectDefinitions)
        {
            CheckInheritance(def, defMap);
        }

        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in dialectDefinitions)
        {
            sources.Add($"{def.Name}Dialect.g.cs", GenerateDialectClass(def, defMap));
        }

        Directory.CreateDirectory(outputDir);
        foreach (var stale in Directory.GetFiles(outputDir, "*Dialect.g.cs"))
        {
            if (!sources.ContainsKey(Path.GetFileName(stale)))
            {
                File.Delete(stale);
            }
        }

        foreach (var source in sources)
        {
            File.WriteAllText(Path.Combine(outputDir, source.Key), source.Value, Encoding.UTF8);
        }
    }

    // A base dialect must be one of the inputs (the generated class derives from <base>Dialect), and the chain must end.
    private static void CheckInheritance(DialectDefinition def, Dictionary<string, DialectDefinition> definitions)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { def.Name };
        for (var current = def; current.Inherits is { } baseName;)
        {
            if (!definitions.TryGetValue(baseName, out var parent))
            {
                throw new GeneratorInputException(current.Path, current.Line,
                    $"The dialect '{current.Name}' inherits '{baseName}', which no dialect YAML file defines.");
            }

            if (!visited.Add(parent.Name))
            {
                throw new GeneratorInputException(def.Path, def.Line,
                    $"The dialect '{def.Name}' inherits from itself through '{baseName}'.");
            }

            current = parent;
        }

        var backend = GetInheritedValue(def, definitions, static d => d.Backend);
        if (backend is not ("Cuda" or "Cpp"))
        {
            throw new GeneratorInputException(def.Path, def.Line,
                $"The dialect '{def.Name}' must set Backend to Cuda or Cpp, itself or through Inherits (got '{backend}').");
        }

        if (GetInheritedValue(def, definitions, static d => d.Capabilities) is { } capabilities)
        {
            foreach (var flag in capabilities.Split([',', '|']))
            {
                var name = flag.AsSpan().Trim().ToString();
                if (name.Length > 0 && !SyntaxFacts.IsValidIdentifier(name))
                {
                    throw new GeneratorInputException(def.Path, def.Line,
                        $"The capability '{name}' of the dialect '{def.Name}' is not a valid DialectCapabilities name.");
                }
            }
        }
    }

    private static string GenerateDialectClass(DialectDefinition def, Dictionary<string, DialectDefinition> definitions)
    {
        using var writer = new SourceWriter();

        writer.AppendGeneratedFileHeader();
        writer.Line("using System;");
        writer.Line("using System.Collections.Generic;");
        writer.Line("using Kassyi.GSharp.Compiler.Layout;");
        writer.Line("using Kassyi.GSharp.Compiler.Transpiler;");
        writer.Line("using Kassyi.GSharp.Compiler.Transpiler.Dialects;");
        writer.Line();
        writer.Line("namespace Kassyi.GSharp.Compiler.Dialects;");
        writer.Line();

        var baseClass = string.IsNullOrEmpty(def.Inherits) ? "YamlDialectBase" : $"{def.Inherits}Dialect";

        writer.Line($"public class {def.Name}Dialect : {baseClass}");
        using (writer.Scope())
        {
            writer.Line($"public override string Name => {CSharpStringLiteral.Quote(def.Name + "Dialect")};");

            var backend = GetInheritedValue(def, definitions, d => d.Backend);
            // CheckInheritance has rejected any other value.
            var isCuda = backend == "Cuda";
            writer.Line($"public override bool IsCuda => {(isCuda ? "true" : "false")};");

            // Capabilities
            var capabilities = GetInheritedValue(def, definitions, d => d.Capabilities);
            if (capabilities != null)
            {
                writer.Line($"public override Kassyi.GSharp.Compiler.Transpiler.Dialects.DialectCapabilities Capabilities => {FormatCapabilityFlags(capabilities)};");
            }

            // Layout (ArrayView / Pointer)
            if (def.Layout.TryGetValue("ArrayView", out var arrayViewLayout))
            {
                writer.Line($"public override TypeLayout ArrayViewLayout => new TypeLayout({arrayViewLayout.Size}, {arrayViewLayout.Align});");
            }

            if (def.Layout.TryGetValue("Pointer", out var pointerLayout))
            {
                writer.Line($"public override TypeLayout PointerLayout => new TypeLayout({pointerLayout.Size}, {pointerLayout.Align});");
            }

            // Types
            if (def.Types.Count > 0)
            {
                writer.Line("public override bool TryMapType(string fullyQualifiedName, out string mappedType)");
                using (writer.Scope())
                {
                    writer.Line("switch (fullyQualifiedName)");
                    using (writer.Scope())
                    {
                        foreach (var kvp in def.Types)
                        {
                            writer.Line($"case {CSharpStringLiteral.Quote(kvp.Key)}: mappedType = {CSharpStringLiteral.Quote(kvp.Value.CppType)}; return true;");
                        }
                    }

                    writer.Line("return base.TryMapType(fullyQualifiedName, out mappedType);");
                }

                var hasAnyLayout = false;
                foreach (var kvp in def.Types)
                {
                    if (kvp.Value.HasLayout)
                    {
                        hasAnyLayout = true;
                        break;
                    }
                }

                if (hasAnyLayout)
                {
                    writer.Line("public override bool TryGetMappedTypeLayout(string fullyQualifiedName, out TypeLayout layout)");
                    using (writer.Scope())
                    {
                        writer.Line("switch (fullyQualifiedName)");
                        using (writer.Scope())
                        {
                            foreach (var kvp in def.Types)
                            {
                                if (kvp.Value.Size is { } size && kvp.Value.Align is { } align)
                                {
                                    writer.Line($"case {CSharpStringLiteral.Quote(kvp.Key)}: layout = new TypeLayout({size}, {align}); return true;");
                                }
                            }
                        }

                        writer.Line("return base.TryGetMappedTypeLayout(fullyQualifiedName, out layout);");
                    }
                }
            }

            // Intrinsics
            if (def.Intrinsics.Count > 0)
            {
                writer.Line(
                    "protected override bool TryMapIntrinsicCore(string signature, out string mappedExpression)");
                using (writer.Scope())
                {
                    writer.Line("switch (signature)");
                    using (writer.Scope())
                    {
                        foreach (var kvp in def.Intrinsics)
                        {
                            writer.Line($"case {CSharpStringLiteral.Quote(kvp.Key)}: mappedExpression = {CSharpStringLiteral.Quote(kvp.Value)}; return true;");
                        }
                    }

                    writer.Line("return base.TryMapIntrinsicCore(signature, out mappedExpression);");
                }
            }
        }

        return writer.ToString();
    }

    // Converts YAML flags such as "HardwareAtomics, SharedMemory" (',' or '|' separated) into a C# flags OR expression.
    private static string FormatCapabilityFlags(string capabilities)
    {
        const string EnumType = "Kassyi.GSharp.Compiler.Transpiler.Dialects.DialectCapabilities";
        using var expression = new SourceWriter();
        var start = 0;
        for (var i = 0; i <= capabilities.Length; i++)
        {
            if (i < capabilities.Length && capabilities[i] != ',' && capabilities[i] != '|')
            {
                continue;
            }

            var name = capabilities.AsSpan(start, i - start).Trim();
            start = i + 1;
            if (name.IsEmpty)
            {
                continue;
            }

            expression.AppendIf(expression.Length > 0, " | ");
            expression.Append($"{EnumType}.{name.ToString()}");
        }

        return expression.Length > 0 ? expression.ToString() : EnumType + ".None";
    }

    private static string? GetInheritedValue(DialectDefinition def, Dictionary<string, DialectDefinition> definitions,
        Func<DialectDefinition, string?> selector)
    {
        var val = selector(def);
        if (!string.IsNullOrEmpty(val))
        {
            return val;
        }

        if (!string.IsNullOrEmpty(def.Inherits) && definitions.TryGetValue(def.Inherits ?? "", out var parent))
        {
            return GetInheritedValue(parent, definitions, selector);
        }

        return null;
    }
}
