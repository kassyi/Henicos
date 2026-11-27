using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Emits <c>GSharp.StateMachineFallbacks.g.cs</c> when a stack switch is active and at least one kernel or suspending
/// device function stays on the CPU state machine.
/// </summary>
internal static class StateMachineFallbackPipeline
{
    /// <summary>
    /// The tracking name of the output step, kept from the managed failure list it replaces: the caching snapshots of
    /// the generator (<c>GpuKernelGeneratorCachingTests</c>) list it.
    /// </summary>
    public const string TrackingName = "GpuManagedStackFailuresInput";

    /// <summary>The generated file that lists the functions staying on the state machine under a stack switch.</summary>
    public const string HintName = "GSharp.StateMachineFallbacks.g.cs";

    private readonly record struct FallbackOutputInput(
        bool EmitSource,
        EquatableArray<StateMachineFallback> Fallbacks);

    /// <summary>Registers the unified state-machine fallback output.</summary>
    public static void Register(
        IncrementalGeneratorInitializationContext context,
        IncrementalValueProvider<ImmutableArray<TranspiledKernelResult>> allKernels,
        IncrementalValueProvider<ImmutableArray<TranspiledDeviceMethodResult>> allDevices,
        IncrementalValueProvider<ManagedStackForms> stackForms,
        IncrementalValueProvider<GeneratorConfig> config)
    {
        var nativeKernelFallbacks = allKernels.Select(static (kernels, _) =>
        {
            var builder = ImmutableArray.CreateBuilder<StateMachineFallback>();
            foreach (var kernel in kernels)
            {
                if (kernel.NativeFallback.HasValue)
                {
                    builder.Add(kernel.NativeFallback);
                }
            }

            return builder.ToImmutable().AsEquatableArray();
        });

        var nativeDeviceFallbacks = allDevices.Select(static (devices, _) =>
        {
            var builder = ImmutableArray.CreateBuilder<StateMachineFallback>();
            foreach (var device in NativeModuleAssembler.GetDistinctDeviceMethods(devices))
            {
                if (device.NativeFallback.HasValue)
                {
                    builder.Add(device.NativeFallback);
                }
            }

            return builder.ToImmutable().AsEquatableArray();
        });

        var fallbackConfig = config.Select(static (value, _) =>
            (RunsOnNativeStacks: value.RunsOnNativeStacks,
                ManagedOnStack: value.ManagedBarrierModel == ManagedBarrierModel.Stack,
                Active: value.RunsOnNativeStacks || value.ManagedBarrierModel == ManagedBarrierModel.Stack,
                value.EmitSource));
        var managedFallbacks = stackForms
            .Combine(fallbackConfig)
            .Select(static (input, _) => input.Right.ManagedOnStack ? input.Left.Fallbacks : default);

        var outputInput = nativeKernelFallbacks
            .Combine(nativeDeviceFallbacks)
            .Combine(managedFallbacks)
            .Combine(fallbackConfig)
            .Select(static (input, _) =>
                CreateOutputInput(
                    input.Left.Left.Left,
                    input.Left.Left.Right,
                    input.Left.Right,
                    input.Right.Active,
                    input.Right.EmitSource))
            .WithTrackingName(TrackingName);

        context.RegisterSourceOutput(outputInput, static (spc, input) => EmitFallbacks(spc, input));
    }

    private static FallbackOutputInput CreateOutputInput(
        EquatableArray<StateMachineFallback> nativeKernels,
        EquatableArray<StateMachineFallback> nativeDevices,
        EquatableArray<StateMachineFallback> managedFallbacks,
        bool active,
        bool emitSource)
    {
        if (!active)
        {
            return default;
        }

        var count = nativeKernels.Length + nativeDevices.Length + managedFallbacks.Length;
        if (count == 0)
        {
            return default;
        }

        var builder = ImmutableArray.CreateBuilder<StateMachineFallback>(count);
        nativeKernels.AsStackEnumerable().AddTo(builder);
        nativeDevices.AsStackEnumerable().AddTo(builder);
        managedFallbacks.AsStackEnumerable().AddTo(builder);
        builder.Sort(StateMachineFallback.Comparer.Instance);
        return new FallbackOutputInput(emitSource, builder.MoveToImmutable().AsEquatableArray());
    }

    private static void EmitFallbacks(SourceProductionContext context, FallbackOutputInput input)
    {
        if (input.Fallbacks.IsDefaultOrEmpty)
        {
            return;
        }

        using var writer = new SourceWriter();
        using var listWriter = new SourceWriter();
        writer.AppendGeneratedFileHeader(enableNullable: false);
        writer.AppendLine("// G#: these functions run as state machines under a stack switch");
        foreach (var fallback in input.Fallbacks)
        {
            writer.Append("// ");
            AppendRow(in writer, fallback);
            if (input.EmitSource)
            {
                AppendRow(in listWriter, fallback);
            }
        }

        if (input.EmitSource)
        {
            writer.AppendLine("namespace Kassyi.GSharp.Generated");
            using (writer.Scope())
            {
                writer.AppendGeneratedCodeAttributes();
                writer.AppendLine("internal static class GSharpStateMachineFallbacks");
                using (writer.Scope())
                {
                    writer.Append("public const string Source = @\"");
                    writer.AppendVerbatimLiteral(listWriter.ToString());
                    writer.AppendLine("\";");
                }
            }
        }

        context.AddSource(HintName, SourceText.From(writer.ToString(), Encoding.UTF8));
    }

    private static void AppendRow(in SourceWriter writer, in StateMachineFallback fallback)
    {
        writer.Append(fallback.Reason.PathName);
        writer.Append('\t');
        writer.Append(fallback.MethodName);
        writer.Append('\t');
        fallback.Reason.AppendFormattedReason(in writer);
        writer.AppendLine();
    }
}
