using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Aot;

/// <summary>
/// Builds the native module of the assembly (<c>.g.cu</c> / <c>.g.cpp</c>, which the build compiles ahead of time)
/// from the transpiled kernels and device functions, and hands its files to <see cref="AotArtifactWriter"/>.
/// </summary>
internal static class AotModuleWriter
{
    /// <summary>Writes the native module of <paramref name="target"/>, or the empty manifest when it has no kernels.</summary>
    public static void Write(
        SourceProductionContext context,
        AotTarget target,
        ImmutableArray<TranspiledKernelResult> kernelMethods,
        ImmutableArray<TranspiledDeviceMethodResult> deviceMethods,
        ImportedDeviceCode imports,
        GeneratorConfig config)
    {
        if (kernelMethods.IsDefaultOrEmpty)
        {
            AotArtifactWriter.WriteEmptyManifest(target, config, context);
            return;
        }

        var isCuda = config.IsCuda;
        var distinctDeviceMethods = NativeModuleAssembler.GetDistinctDeviceMethods(deviceMethods);
        var commonDeviceSource = imports.Source + NativeModuleAssembler.AssembleDeviceSource(distinctDeviceMethods, isCuda);
        var preludeSource = BuildPreludeSource(config);
        var symbolSites = CollectSymbolSites(context, kernelMethods);
        var kernelSources = CollectKernelSources(kernelMethods, isCuda, out var kernelItems);

        var dialectName = isCuda ? "Cuda" : "Cpu";
        var partitionPlan = NativeModulePartitioner.Plan(kernelItems.AsSpan());
        var partitionJson = NativeModulePlanJson.Format(
            dialectName, partitionPlan, kernelItems.AsSpan());

        var requiredIncludes = NativeModuleAssembler.CollectIncludes(kernelMethods, distinctDeviceMethods, isCuda);
        imports.Includes
            .AsStackEnumerable()
            .AddTo(requiredIncludes);
        var moduleStem = AotModuleNames.GetArtifactFileStem(target.ModuleName);
        var splitPlan = NativeModuleSplitter.Plan(moduleStem, isCuda, partitionPlan);
        var moduleFiles = NativeModuleSplitter.Emit(
            splitPlan, requiredIncludes, preludeSource, commonDeviceSource, kernelSources);
        AotArtifactWriter.Write(target, config, moduleFiles, context, imports.FindFullName,
            symbolSites, partitionJson);
    }

    private static string BuildPreludeSource(GeneratorConfig config)
    {
        var preludeSource = NativeModuleAssembler.GetPreludeSource(in config);
        if (config.IsCuda && config.IsCheckedBuild)
        {
            preludeSource = "#define GSHARP_CHECKED 1\n" + preludeSource;
        }

        return preludeSource;
    }

    /// <summary>
    /// Gathers the error sites of all kernels into the symbol file records, and reports a collision of two different
    /// sites that share an ID. A collision leaves the symbol file without error sites.
    /// </summary>
    private static ImmutableArray<GSharpNameEncoding.SymbolErrorSiteRecord> CollectSymbolSites(
        SourceProductionContext context,
        ImmutableArray<TranspiledKernelResult> kernelMethods)
    {
        var symbolSites = ImmutableArray.CreateBuilder<GSharpNameEncoding.SymbolErrorSiteRecord>();
        var rawSites = ImmutableArray.CreateBuilder<ErrorSiteEntry>();
        foreach (var method in kernelMethods)
        {
            if (!method.ErrorSites.IsDefaultOrEmpty)
            {
                foreach (var site in method.ErrorSites)
                {
                    rawSites.Add(site);
                }
            }
        }

        if (!ErrorSiteTable.TryBuildModuleSites(rawSites, out var moduleSites, out var c1, out var c2))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                GSharpDiagnostics.ErrorSiteIdCollision,
                Location.None,
                c1.SiteId,
                c1.MemberName, c1.File, c1.Line, c1.Column,
                c2.MemberName, c2.File, c2.Line, c2.Column));
        }
        else
        {
            foreach (var site in moduleSites)
            {
                symbolSites.Add(new GSharpNameEncoding.SymbolErrorSiteRecord(
                    site.SiteId,
                    site.File,
                    site.Line,
                    site.Column,
                    site.MemberName,
                    site.DefaultMessage));
            }
        }

        return symbolSites.ToImmutable();
    }

    /// <summary>Gets the source of every kernel, and the size of each for the partition plan.</summary>
    private static string[] CollectKernelSources(
        ImmutableArray<TranspiledKernelResult> kernelMethods,
        bool isCuda,
        out KernelPartitionItem[] kernelItems)
    {
        var kernelSources = new string[kernelMethods.Length];
        kernelItems = new KernelPartitionItem[kernelMethods.Length];
        for (var i = 0; i < kernelMethods.Length; i++)
        {
            var method = kernelMethods[i];
            var source = method.GetSource(isCuda);
            kernelSources[i] = source;
            var byteCount = Encoding.UTF8.GetByteCount(source);
            kernelItems[i] = new KernelPartitionItem(method.Class.FullyQualifiedName, byteCount);
        }

        return kernelSources;
    }
}
