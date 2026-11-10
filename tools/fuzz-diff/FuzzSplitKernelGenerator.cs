using System.Globalization;

namespace Kassyi.GSharp.Tools.FuzzDiff;

/// <summary>What a split kernel takes from its batch: the options, the known-issue switches and the device
/// functions.</summary>
internal sealed record FuzzSplitGenerationContext(
    FuzzOptions Options,
    KnownIssueFilter IssueFilter,
    IReadOnlyList<DeviceFunctionSpec> DeviceFunctions);

/// <summary>
/// Generates one split kernel and its <c>DifferentialCase</c>, which launches every lane of two full blocks and checks
/// the kernel against its oracle twin (<see cref="FuzzSplitOracle"/>). The batch must declare <see
/// cref="ScalarFuzzGenerator.SplitHelperSources"/>.
/// </summary>
internal static class FuzzSplitKernelGenerator
{
    /// <summary>Generates the split kernel <paramref name="kernelName"/> with the split points of
    /// <paramref name="forms"/>.</summary>
    public static GeneratedKernelSpec Generate(
        FuzzSplitGenerationContext context,
        string kernelName,
        ulong kernelSeed,
        int size,
        FuzzSplitForms forms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kernelName);
        if (forms == FuzzSplitForms.None)
        {
            throw new ArgumentException("A split kernel needs at least one split form.", nameof(forms));
        }

        size = Math.Clamp(size, 1, 8);
        var state = CreateState(context, kernelSeed, size);
        FuzzSplitKernelEmitter.EmitSplitStatements(state, forms);

        return new GeneratedKernelSpec(
            kernelName,
            kernelSeed,
            size,
            FuzzSplitKernelEmitter.RenderKernelMethod(kernelName, kernelSeed, size, forms, state),
            RenderCase(context.Options, kernelName, kernelSeed),
            LocalNames(state),
            state.Statements.ToArray(),
            forms);
    }

    private static KernelGenerationState CreateState(FuzzSplitGenerationContext context, ulong kernelSeed, int size)
    {
        var rng = new FuzzRandom(kernelSeed);
        return new KernelGenerationState(
            rng,
            size,
            Math.Clamp(context.Options.InterferenceProbability, 0.25, 0.60),
            context.IssueFilter,
            FuzzSwarmConfig.Create(rng),
            context.Options.KernelClassName,
            context.DeviceFunctions);
    }

    private static string RenderCase(FuzzOptions options, string kernelName, ulong kernelSeed)
    {
        string caseHeader = string.Create(
            CultureInfo.InvariantCulture,
            $"// fuzz-seed: {FuzzRandom.FormatSeed(kernelSeed)}");
        return FuzzCaseEmitter.RenderDifferentialCaseProperty(
            options.KernelClassName,
            kernelName,
            options.InputSeed,
            caseHeader,
            split: true);
    }

    private static List<string> LocalNames(KernelGenerationState state)
    {
        var localNames = new List<string>(state.Locals.Count);
        foreach (var local in state.Locals)
        {
            localNames.Add(local.Name);
        }

        return localNames;
    }
}
