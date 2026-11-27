using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The incremental steps of the managed stack model: the stack forms of a compilation (kernels when
/// <c>GSharpManagedBarrierModel=Stack</c>, and suspending device functions also when <c>GSharpExportDeviceCode</c> is
/// enabled), the kernels they attach to, and the device forms that go next to the state machines.
/// </summary>
/// <remarks>
/// [WHY] Kernels get stack forms only under <c>GSharpManagedBarrierModel=Stack</c>; device functions also get them
/// whenever <c>GSharpExportDeviceCode</c> is enabled so that a consumer built with <c>Stack</c> can call them
/// directly. State-machine fallbacks are still emitted only under <c>GSharpManagedBarrierModel=Stack</c>.
/// </remarks>
internal static class ManagedStackPipeline
{
    /// <summary>
    /// The tracking name of the step that makes the stack forms of the managed stack model. It reads the compilation
    /// when <c>GSharpManagedBarrierModel=Stack</c> or <c>GSharpExportDeviceCode</c> is enabled; otherwise it returns
    /// nothing.
    /// </summary>
    public const string FormsTrackingName = "GpuManagedStackForms";

    /// <summary>The stack forms of the compilation under the configured model.</summary>
    /// <remarks>The step is tracked as <see cref="FormsTrackingName"/>.</remarks>
    public static IncrementalValueProvider<ManagedStackForms> Forms(IncrementalValueProvider<Compilation> compilation,
        IncrementalValueProvider<GeneratorConfig> config) =>
        compilation
            .Combine(config.Select(static (value, _) => (value.ManagedBarrierModel, value.ExportDeviceCode)))
            .Select(static (input, cancellationToken) =>
                ManagedStackFormCollector.Collect(input.Left, input.Right.ManagedBarrierModel,
                    input.Right.ExportDeviceCode, cancellationToken))
            .WithTrackingName(FormsTrackingName);

    /// <summary>The kernels with their stack forms attached (<see cref="ManagedStackFormCollector.Attach"/>).</summary>
    public static IncrementalValueProvider<ImmutableArray<TranspiledKernelResult>> AttachToKernels(
        IncrementalValueProvider<ImmutableArray<TranspiledKernelResult>> kernels,
        IncrementalValueProvider<ManagedStackForms> forms) =>
        kernels
            .Combine(forms)
            .Select(static (input, _) => ManagedStackFormCollector.Attach(input.Left, input.Right));

    /// <summary>
    /// The sources that go into the device state machine file of <paramref name="group"/>: its state machines, then
    /// its stack forms when the model is <see cref="ManagedBarrierModel.Stack"/> or <c>GSharpExportDeviceCode</c> is
    /// enabled; none when there is nothing to emit.
    /// </summary>
    public static IEnumerable<string>? DeviceSources(DeviceStateMachineGroup group, GeneratorConfig config)
    {
        var includeStack = (config.ManagedBarrierModel == ManagedBarrierModel.Stack || config.ExportDeviceCode) &&
                           !group.StackSources.IsDefaultOrEmpty;
        if (group.Sources.IsDefaultOrEmpty && !includeStack)
        {
            return null;
        }

        return includeStack ? Concat(group.Sources, group.StackSources) : group.Sources;
    }

    private static IEnumerable<string> Concat(EquatableArray<string> first, EquatableArray<string> second)
    {
        foreach (var source in first)
        {
            yield return source;
        }

        foreach (var source in second)
        {
            yield return source;
        }
    }
}
