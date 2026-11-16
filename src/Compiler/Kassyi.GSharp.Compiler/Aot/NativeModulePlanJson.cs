namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Writes a <see cref="NativeModulePlan"/> as the <c>{artifact stem}.partition.json</c> file next to the native module:
/// the dialect, B, the bytes and class count of each unit, and the unit of each class, sorted by class name.
/// </summary>
internal static class NativeModulePlanJson
{
    /// <summary>
    /// Formats <paramref name="plan"/>, which was made from <paramref name="kernels"/> in the same order; the unit of a
    /// class is the one the plan gives its kernels.
    /// </summary>
    public static string Format(string dialect, NativeModulePlan plan, ReadOnlySpan<KernelPartitionItem> kernels)
    {
        var classUnits = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < kernels.Length; i++)
        {
            classUnits[kernels[i].ClassFullName] = plan.KernelUnitIndices[i];
        }

        var unitClassCounts = new int[plan.UnitCount];
        foreach (var unit in classUnits.Values)
        {
            unitClassCounts[unit]++;
        }

        var sortedClasses = new string[classUnits.Count];
        classUnits.Keys.CopyTo(sortedClasses, 0);
        Array.Sort(sortedClasses, StringComparer.Ordinal);

        using var writer = new SourceWriter();
        writer.AppendLine("{");
        writer.AppendLine($"  \"dialect\": \"{GSharpNameEncoding.EscapeJsonString(dialect)}\",");
        writer.AppendLine($"  \"B\": {plan.UnitCount},");
        writer.AppendLine("  \"units\": [");
        for (var i = 0; i < plan.UnitCount; i++)
        {
            writer.AppendLine("    {");
            writer.AppendLine($"      \"unit\": {i},");
            writer.AppendLine($"      \"bytes\": {plan.UnitByteCounts[i]},");
            writer.AppendLine($"      \"class_count\": {unitClassCounts[i]}");
            writer.AppendLine(i + 1 < plan.UnitCount ? "    }," : "    }");
        }

        writer.AppendLine("  ],");
        writer.AppendLine("  \"classes\": [");
        for (var i = 0; i < sortedClasses.Length; i++)
        {
            var className = sortedClasses[i];
            writer.AppendLine("    {");
            writer.AppendLine($"      \"class\": \"{GSharpNameEncoding.EscapeJsonString(className)}\",");
            writer.AppendLine($"      \"unit\": {classUnits[className]}");
            writer.AppendLine(i + 1 < sortedClasses.Length ? "    }," : "    }");
        }

        writer.AppendLine("  ]");
        writer.AppendLine("}");
        return writer.ToString();
    }
}
