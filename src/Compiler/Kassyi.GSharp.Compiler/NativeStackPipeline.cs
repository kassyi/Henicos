using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The incremental steps that give the kernel and device function translations the native stack switch
/// (<see cref="GeneratorConfig.RunsOnNativeStacks"/>).
/// </summary>
/// <remarks>
/// The stack form is translated only when the build asks for it (<c>GSharpNativeBarrierModel=Stack</c> on a C++
/// dialect), so the translation takes the switch: the attribute's context passes through to a step that sees the
/// configuration. The transform reruns on every compilation anyway, so this loses no caching; the tracked steps are the
/// translations, whose results compare by value as before.
/// </remarks>
internal static class NativeStackPipeline
{
    /// <summary>
    /// The tracking name of the step that decides whether the kernels and device functions are also translated into
    /// their native stack form (<see cref="GeneratorConfig.RunsOnNativeStacks"/>).
    /// </summary>
    public const string SwitchTrackingName = "GpuNativeStackSwitch";

    /// <summary>
    /// The attribute's context on its way to the translation step. A class compares by reference: the context holds
    /// the semantic model of one compilation, and the translation reruns on each one anyway.
    /// </summary>
    internal sealed class AttributeTarget(GeneratorAttributeSyntaxContext context)
    {
        public GeneratorAttributeSyntaxContext Context { get; } = context;
    }

    /// <summary>The transform of <c>ForAttributeWithMetadataName</c>: keeps the context for <see cref="Translate{TResult}"/>.</summary>
    public static AttributeTarget Capture(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken) =>
        new(context);

    /// <summary>Whether the build asks for the native stack form, a step of its own so that tests can track it.</summary>
    public static IncrementalValueProvider<bool> Switch(IncrementalValueProvider<GeneratorConfig> config) =>
        config
            .Select(static (value, _) => value.RunsOnNativeStacks)
            .WithTrackingName(SwitchTrackingName);

    /// <summary>Runs <paramref name="translate"/> on each captured context together with the switch.</summary>
    public static IncrementalValuesProvider<TResult> Translate<TResult>(
        IncrementalValuesProvider<AttributeTarget> targets,
        IncrementalValueProvider<bool> runsOnNativeStacks,
        Func<NativeStackContext, CancellationToken, TResult> translate) =>
        targets
            .Combine(runsOnNativeStacks)
            .Select((input, cancellationToken) =>
                translate(new NativeStackContext(input.Left.Context, input.Right), cancellationToken));
}
