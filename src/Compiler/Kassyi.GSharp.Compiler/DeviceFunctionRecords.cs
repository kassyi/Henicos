using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The export records of a transpiled device function: the record of the function, which other assemblies place, and
/// the records of the structs its text defines.
/// </summary>
internal static class DeviceFunctionRecords
{
    /// <summary>
    /// The records of the device function of <paramref name="plan"/>. An iterator has no function record: it is
    /// expanded where it is enumerated, and another assembly gets GSH0003 for it.
    /// </summary>
    /// <remarks>
    /// The records are built apart from the transpilation: an exception here fails the records (with the message,
    /// which a consumer states in GSH0089) and leaves the library's own text as it is.
    /// </remarks>
    public static (DeviceExportRecord Function, EquatableArray<DeviceExportRecord> Structs) Create(
        in MethodEmissionPlan plan, IMethodSymbol method, Compilation compilation, DeviceFunctionTexts texts,
        DeviceFunctionForms forms)
    {
        var isIterator = plan.Kind == MethodEmissionKind.Iterator;
        try
        {
            var function = isIterator
                ? default
                : CreateFunctionRecord(method, compilation,
                    ExportedText(texts.Cuda, compilation, isCuda: true),
                    ExportedText(texts.Cpp, compilation, isCuda: false),
                    forms, texts.Sites);
            var structs = DeviceExportEmitter.CreateStructRecords(compilation, texts.Cuda.Structs, texts.Cpp.Structs);
            return (function, structs);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var function = isIterator
                ? default
                : DeviceMethodResults.CreateFailedFunctionRecord(method, PartFailure.Describe(exception));
            return (function, default);
        }
    }

    // The text of one dialect as the function's record stores it: the signature, and the body without the struct and
    // Dispose definitions written in front of it, which the struct records hold. The definitions of the structs that
    // no record holds stay in front of the body (DeviceExportEmitter.CreateStructRecords, #538 review B1). Empty when
    // the dialect failed (the record says so, and a consumer copies no text of it)
    private static ExportedFunctionText ExportedText(DeviceDialectText text, Compilation compilation, bool isCuda)
    {
        var includes = GpuKernelGenerator.ToIncludes(text.Includes);
        if (text.Failures.HasFailures)
        {
            return new ExportedFunctionText("", "", true, includes, default);
        }

        var structs = text.Structs;
        var prefix = 0;
        foreach (var part in structs.Definitions)
        {
            prefix += part.Text.Length;
        }

        foreach (var part in structs.Disposes)
        {
            prefix += part.Text.Length;
        }

        var kept = ImmutableArray.CreateBuilder<string>();
        var body = DeviceExportEmitter.KeepUnrecorded(structs.Definitions, compilation, isCuda, kept) +
                   DeviceExportEmitter.KeepUnrecorded(structs.Disposes, compilation, isCuda, kept) +
                   text.Source[prefix..];
        return new ExportedFunctionText(text.Signature, body, false, includes, kept.ToImmutable().AsEquatableArray());
    }

    private readonly record struct ExportedFunctionText(
        string Signature,
        string Body,
        bool Failed,
        EquatableArray<string> Includes,
        EquatableArray<string> KeptNames);

    private static DeviceExportRecord.DialectText BuildFunctionDialectText(
        string cppName,
        ExportedFunctionText text) =>
        new(
            References: DeviceExportEmitter.CollectReferences(
                DeviceExportEmitter.OwnNames(cppName, default, text.KeptNames),
                text.Signature,
                text.Body),
            Signature: text.Signature,
            Body: text.Body,
            Dispose: "",
            Includes: text.Includes,
            Failed: text.Failed);

    private static DeviceExportRecord CreateFunctionRecord(IMethodSymbol method, Compilation compilation,
        ExportedFunctionText cuda, ExportedFunctionText cpp, DeviceFunctionForms forms,
        EquatableArray<ErrorSiteEntry> errorSites)
    {
        var cppName = GlobalCppNames.GetFunctionName(method);
        var sites = DynamicSharedMemoryAnalysis.GetReachableSites(method, compilation);
        var siteNames = ImmutableArray.CreateBuilder<string>(sites.Length);
        foreach (var site in sites)
        {
            siteNames.Add(site.ToString("x16", CultureInfo.InvariantCulture));
        }

        // B7: which parameters make a barrier diverge, for a caller in another assembly
        var divergence = UniformityAnalysis.GetExportedDivergence(method, compilation);

        var names = new DeviceExportRecord.ExportNames(
            CppName: cppName,
            CppFullName: GlobalCppNames.TryGetFullName(cppName, out var fullName) ? fullName : "",
            DefinedNames: default,
            DefinedIds: default,
            DefinedFullNames: default);

        var traits = new DeviceExportRecord.FunctionTraits(
            FrameName: forms.FrameName,
            FrameDependencies: forms.Frames,
            Suspends: SuspensionAnalysis.IsSuspendingFunction(method, compilation),
            ReachesBlockBarrier: SuspensionAnalysis.ReachesBlockBarrier(method, compilation),
            IsParallelDependent: ContextInferenceEngine.IsParallelDependent(method,
                new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default), compilation),
            ReachesDynamicSharedMemory: DynamicSharedMemoryAnalysis.ReachesDynamicSharedMemory(method, compilation),
            DynamicSharedMemorySites: siteNames.MoveToImmutable().AsEquatableArray(),
            Effect: DeviceExportEmitter.FormatEffect(DeviceFunctionEffects.Classify(method, compilation)),
            // The records and the state machines are emitted together (GSharpExportDeviceCode)
            ManagedStateMachine: forms.Managed.StateMachine,
            ManagedVersion: forms.Managed.Version,
            DivergentConditionParameters: divergence.ConditionParameters,
            DivergentExitOnlyParameters: divergence.ExitOnlyParameters,
            HasExitOnlyBarrier: divergence.HasExitOnlyBarrier,
            ReachesArrivalBarrier: SuspensionAnalysis.ReachesArrivalBarrier(method, compilation),
            ReachesSharedMemory: CSharpExecutionAnalysis.ReachesSharedMemory(method, compilation),
            ReachesGeneratedMeaning: CSharpExecutionAnalysis.ReachesGeneratedMeaning(method, compilation),
            PrivateManagedFrame: forms.Managed.PrivateFrame,
            ReachesWarpShuffle: SuspensionAnalysis.ReachesWarpShuffleOnStack(method, compilation),
            ReachesActiveMask: SuspensionAnalysis.ReachesActiveMaskOnStack(method, compilation),
            ReachesGridBarrier: SuspensionAnalysis.ReachesGridBarrierOnStack(method, compilation));

        return new DeviceExportRecord(
            DeviceExportRecord.FunctionKind,
            method.OriginalDefinition.GetDocumentationCommentId() ?? cppName,
            names,
            Cuda: BuildFunctionDialectText(cppName, cuda),
            Cpp: BuildFunctionDialectText(cppName, cpp),
            traits,
            InternalError: "",
            ErrorSites: EncodeErrorSites(errorSites));
    }

    // The error sites as the record stores them (ErrorSiteCodec)
    private static EquatableArray<string> EncodeErrorSites(EquatableArray<ErrorSiteEntry> sites)
    {
        if (sites.IsDefaultOrEmpty)
        {
            return default;
        }

        var encoded = ImmutableArray.CreateBuilder<string>(sites.Length);
        foreach (var site in sites)
        {
            encoded.Add(ErrorSiteCodec.Encode(site));
        }

        return encoded.MoveToImmutable().AsEquatableArray();
    }
}
