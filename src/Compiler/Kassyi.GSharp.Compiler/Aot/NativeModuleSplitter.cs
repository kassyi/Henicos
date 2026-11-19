using System.Collections.Immutable;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Splits a native module into a shared header and B unit files when building CPU with B &gt; 1, while keeping
/// single-unit CPU modules and all CUDA modules as one file with no header.
/// </summary>
internal static class NativeModuleSplitter
{
    /// <summary>
    /// Plans the output file names and per-unit kernel index lists from <paramref name="partitionPlan"/>.
    /// </summary>
    public static NativeModuleSplitPlan Plan(string moduleStem, bool isCuda, NativeModulePlan partitionPlan)
    {
        if (partitionPlan.UnitCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionPlan), "UnitCount must be at least 1.");
        }

        var isSplit = !isCuda && partitionPlan.UnitCount > 1;
        var effectiveUnitCount = isSplit ? partitionPlan.UnitCount : 1;
        var ext = isCuda ? ".g.cu" : ".g.cpp";
        var headerFileName = isSplit ? moduleStem + ".g.hpp" : null;

        var unitFileNames = ImmutableArray.CreateBuilder<string>(effectiveUnitCount);
        unitFileNames.Add(moduleStem + ext);
        for (var i = 1; i < effectiveUnitCount; i++)
        {
            unitFileNames.Add($"{moduleStem}.{i}{ext}");
        }

        var kernelCount = partitionPlan.KernelUnitIndices.Length;
        var counts = new int[effectiveUnitCount];
        for (var k = 0; k < kernelCount; k++)
        {
            var unitIndex = isSplit ? partitionPlan.KernelUnitIndices[k] : 0;
            if ((uint)unitIndex >= (uint)effectiveUnitCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(partitionPlan),
                    $"Kernel unit index {unitIndex} is out of range [0, {effectiveUnitCount}).");
            }

            counts[unitIndex]++;
        }

        var builders = new ImmutableArray<int>.Builder[effectiveUnitCount];
        for (var u = 0; u < effectiveUnitCount; u++)
        {
            builders[u] = ImmutableArray.CreateBuilder<int>(counts[u]);
        }

        for (var k = 0; k < kernelCount; k++)
        {
            var unitIndex = isSplit ? partitionPlan.KernelUnitIndices[k] : 0;
            builders[unitIndex].Add(k);
        }

        var unitKernelIndices = ImmutableArray.CreateBuilder<ImmutableArray<int>>(effectiveUnitCount);
        for (var u = 0; u < effectiveUnitCount; u++)
        {
            unitKernelIndices.Add(builders[u].MoveToImmutable());
        }

        return new NativeModuleSplitPlan(
            headerFileName,
            unitFileNames.MoveToImmutable(),
            unitKernelIndices.MoveToImmutable());
    }

    /// <summary>
    /// Materializes <paramref name="splitPlan"/> into <see cref="NativeModuleFiles"/>, emitting the header and each
    /// unit sequentially so <see cref="SourceWriter"/> never nests across units.
    /// </summary>
    public static NativeModuleFiles Emit(
        NativeModuleSplitPlan splitPlan,
        HashSet<string> requiredIncludes,
        string preludeSource,
        string commonDeviceSource,
        IReadOnlyList<string> kernelSources)
    {
        if (splitPlan.HeaderFileName is not { } headerFileName)
        {
            var body = NativeModuleAssembler.AssembleBody(
                requiredIncludes, preludeSource, commonDeviceSource, kernelSources);
            return new NativeModuleFiles(
                null,
                [new NativeModuleFile(splitPlan.UnitFileNames[0], body, IncludeErrorSites: true)]);
        }

        var headerBody = NativeModuleAssembler.AssembleHeader(requiredIncludes, preludeSource, commonDeviceSource);
        var header = new NativeModuleFile(headerFileName, headerBody, IncludeErrorSites: true);
        var unitCount = splitPlan.UnitFileNames.Length;
        var units = ImmutableArray.CreateBuilder<NativeModuleFile>(unitCount);
        for (var i = 0; i < unitCount; i++)
        {
            var unitBody = NativeModuleAssembler.AssembleUnit(
                headerFileName, kernelSources, splitPlan.UnitKernelIndices[i]);
            units.Add(new NativeModuleFile(splitPlan.UnitFileNames[i], unitBody, IncludeErrorSites: false));
        }

        return new NativeModuleFiles(header, units.MoveToImmutable());
    }
}
