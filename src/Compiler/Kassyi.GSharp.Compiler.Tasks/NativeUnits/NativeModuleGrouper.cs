namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>Groups the C++ source items of a build into native modules by their file name.</summary>
internal static class NativeModuleGrouper
{
    /// <summary>Groups the sources by module. Logs an error and returns <c>null</c> for an invalid source.</summary>
    public static NativeModuleGroup[]? Group(ITaskItem[] sources, StringComparer pathComparer, TaskLoggingHelper log)
    {
        var parsed = new (string SourcePath, string ModuleStem, string UnitStem)[sources.Length];
        var parsedCount = 0;
        var seenSources = new HashSet<string>(pathComparer);
        var unitSourcesByStem = new Dictionary<string, string>(pathComparer);
        var moduleIndices = new Dictionary<string, int>(pathComparer);
        var moduleStems = new string[sources.Length];
        var unitCounts = new int[sources.Length];
        var moduleCount = 0;

        for (var i = 0; i < sources.Length; i++)
        {
            var rawPath = sources[i].GetMetadata("FullPath");
            var candidatePath = !string.IsNullOrEmpty(rawPath) ? rawPath : sources[i].ItemSpec;
            if (string.IsNullOrWhiteSpace(candidatePath))
            {
                log.LogError("Invalid C++ source entry: source path cannot be empty.");
                return null;
            }

            var sourcePath = Path.GetFullPath(candidatePath);
            if (!seenSources.Add(sourcePath))
            {
                continue;
            }

            var (moduleStem, unitStem) = ArtifactHelper.GetNativeModuleAndUnitStem(sourcePath);
            if (string.IsNullOrEmpty(moduleStem) || string.IsNullOrEmpty(unitStem))
            {
                log.LogError($"Invalid C++ source path '{sourcePath}': expected a non-empty file stem.");
                return null;
            }

            if (unitSourcesByStem.TryGetValue(unitStem, out var existingSource))
            {
                log.LogError(
                    $"Multiple C++ sources resolve to the same object unit stem '{unitStem}': " +
                    $"'{existingSource}' and '{sourcePath}'.");
                return null;
            }

            unitSourcesByStem[unitStem] = sourcePath;
            parsed[parsedCount++] = (sourcePath, moduleStem, unitStem);

            if (!moduleIndices.TryGetValue(moduleStem, out var moduleIndex))
            {
                moduleIndex = moduleCount++;
                moduleIndices[moduleStem] = moduleIndex;
                moduleStems[moduleIndex] = moduleStem;
            }

            unitCounts[moduleIndex]++;
        }

        return BuildModules(parsed, parsedCount, moduleStems, unitCounts, moduleIndices, moduleCount);
    }

    private static NativeModuleGroup[] BuildModules(
        (string SourcePath, string ModuleStem, string UnitStem)[] parsed,
        int parsedCount,
        string[] moduleStems,
        int[] unitCounts,
        Dictionary<string, int> moduleIndices,
        int moduleCount)
    {
        var modules = new NativeModuleGroup[moduleCount];
        for (var i = 0; i < moduleCount; i++)
        {
            modules[i] = new NativeModuleGroup(moduleStems[i], new NativeUnitSource[unitCounts[i]]);
            unitCounts[i] = 0;
        }

        for (var i = 0; i < parsedCount; i++)
        {
            var entry = parsed[i];
            var moduleIndex = moduleIndices[entry.ModuleStem];
            modules[moduleIndex].Units[unitCounts[moduleIndex]++] =
                new NativeUnitSource(entry.SourcePath, entry.UnitStem);
        }

        return modules;
    }
}
