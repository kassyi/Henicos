namespace Kassyi.GSharp.Compiler.Tasks.NativeUnits;

/// <summary>
/// Decides, without compiling anything, which native translation units need compiling and which objects each
/// module links. The result is plain data that the task then executes.
/// </summary>
internal static class NativeUnitBuildPlanner
{
    /// <summary>Plans the build of every module for every architecture.</summary>
    public static NativeUnitBuildPlan Plan(
        NativeModuleGroup[] modules,
        string[] architectures,
        NativeUnitBuildSettings settings,
        INativeToolchain toolchain,
        TaskLoggingHelper log,
        CancellationToken cancellationToken)
    {
        var pendingUnits = new List<NativeUnitCompileWork>();
        var upToDateUnits = new List<NativeUnitArtifacts>();
        var linkTargets = new List<NativeLinkTarget>(modules.Length * architectures.Length);

        foreach (var arch in architectures)
        {
            var archObjDir = Path.Combine(settings.ObjectRoot, arch);
            Directory.CreateDirectory(archObjDir);
            var target = new ArchitectureTarget(
                arch, archObjDir, toolchain.GetCompilerIdentity(arch, cancellationToken));

            foreach (var module in modules)
            {
                linkTargets.Add(PlanModule(module, target, settings, toolchain, log, pendingUnits, upToDateUnits));
            }
        }

        return new NativeUnitBuildPlan([.. pendingUnits], [.. upToDateUnits], [.. linkTargets]);
    }

    /// <summary>One architecture of the build: its object directory and the identity of the compiler it uses.</summary>
    private readonly record struct ArchitectureTarget(string Arch, string ObjDir, CompilerIdentity CompilerIdentity);

    private static NativeLinkTarget PlanModule(
        NativeModuleGroup module,
        ArchitectureTarget target,
        NativeUnitBuildSettings settings,
        INativeToolchain toolchain,
        TaskLoggingHelper log,
        List<NativeUnitCompileWork> pendingUnits,
        List<NativeUnitArtifacts> upToDateUnits)
    {
        var headerHashes = new Dictionary<string, string>(settings.PathComparer);
        var unitObjPaths = new string[module.Units.Length];

        for (var i = 0; i < module.Units.Length; i++)
        {
            var work = PlanUnit(module.Units[i], module.ModuleStem, target, headerHashes, settings, toolchain);
            unitObjPaths[i] = work.Artifacts.ObjPath;
            if (IsUpToDate(work, log))
            {
                upToDateUnits.Add(work.Artifacts);
            }
            else
            {
                pendingUnits.Add(work);
            }
        }

        return new NativeLinkTarget(
            target.Arch,
            unitObjPaths,
            Path.Combine(settings.OutputDirectory, $"{module.ModuleStem}_{target.Arch}.dll"));
    }

    private static NativeUnitCompileWork PlanUnit(
        NativeUnitSource unit,
        string moduleStem,
        ArchitectureTarget target,
        Dictionary<string, string> headerHashes,
        NativeUnitBuildSettings settings,
        INativeToolchain toolchain)
    {
        var arch = target.Arch;
        var objPath = Path.Combine(target.ObjDir, $"{unit.UnitStem}{settings.ObjectExtension}");
        var recordPath = objPath + CppObjectCommandRecord.FileExtension;
        string? vcPdbPath = settings.UsesPerUnitVcPdb
            ? Path.Combine(target.ObjDir, $"{unit.UnitStem}.vc.pdb")
            : null;

        var headerPath = CppObjectCommandRecord.GetModuleHeaderPath(unit.SourcePath, moduleStem);
        if (!headerHashes.TryGetValue(headerPath, out var headerHash))
        {
            headerHash = CppObjectCommandRecord.ComputeFileSha256Hex(
                headerPath,
                CppObjectCommandRecord.MissingHeaderSentinel);
            headerHashes[headerPath] = headerHash;
        }

        var compileArgs = toolchain.BuildCompileObjectArguments(
            unit.SourcePath, objPath, arch, settings.Sanitizers, vcPdbPath, settings.RunsOnNativeStacks);
        var unitHash = CppObjectCommandRecord.ComputeFileSha256Hex(
            unit.SourcePath,
            CppObjectCommandRecord.MissingUnitSentinel);
        var record = CppObjectCommandRecord.Build(
            arch, target.CompilerIdentity, compileArgs, unitHash, headerHash);

        return new NativeUnitCompileWork(
            unit.SourcePath, arch, new NativeUnitArtifacts(objPath, recordPath, vcPdbPath), record);
    }

    private static bool IsUpToDate(NativeUnitCompileWork work, TaskLoggingHelper log) =>
        NativeToolchainHelper.HasNonEmptyFile(work.Artifacts.ObjPath) &&
        File.Exists(work.SourcePath) &&
        CommandRecordFile.IsCurrent(
            log, work.Artifacts.RecordPath, work.RecordContent, CppObjectCommandRecord.RecordDescription);
}
