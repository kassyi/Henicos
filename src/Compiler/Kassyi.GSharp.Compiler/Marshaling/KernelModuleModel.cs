using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// A kernel whose function pointer the CUDA module context warms up, as the call the module context writes.
/// </summary>
/// <param name="Type">
/// The <c>global::</c>-qualified type that declares the method: the kernel's class, or the outermost relay host of a
/// class the module context cannot name (<see cref="HostClassHeader.WarmupRelayHosts"/>).
/// </param>
/// <param name="Method">
/// <c>WarmupFunctionPointer{key}</c>, or the relay (<see cref="KernelHostModel.WarmupRelayName"/>).
/// </param>
internal readonly record struct KernelWarmup(string Type, string Method)
{
    public static KernelWarmup Create(in HostClassHeader header, in KernelEmission kernel) =>
        header.WarmupRelayHosts.IsDefaultOrEmpty
            ? new KernelWarmup(header.FullyQualifiedName, KernelHostNames.GetWarmupMethodName(kernel.MemberKey))
            : new KernelWarmup(header.WarmupRelayHosts[0], kernel.Host.WarmupRelayName);
}

/// <summary>
/// A relay method that the generated part of a container of a kernel class declares
/// (<see cref="HostClassHeader.WarmupRelayHosts"/>):
/// <c>internal static void &lt;Name&gt;() =&gt; &lt;TargetType&gt;.&lt;TargetMethod&gt;();</c>.
/// </summary>
/// <param name="OuterIndex">The index of the container in <see cref="HostClassHeader.OuterDeclarations"/>.</param>
/// <param name="Name">The relay (<see cref="KernelHostModel.WarmupRelayName"/>).</param>
/// <param name="TargetType">The next relay host, or the kernel class after the last one.</param>
/// <param name="TargetMethod">
/// The same relay in the next host, or <c>WarmupFunctionPointer{key}</c> in the class.
/// </param>
internal readonly record struct WarmupRelay(int OuterIndex, string Name, string TargetType, string TargetMethod);

/// <summary>Decides the warmup relays of a kernel class (#1282).</summary>
internal static class WarmupRelayPlan
{
    /// <summary>
    /// The relays of the kernels of <paramref name="header"/>'s class that have a warmup
    /// (<see cref="KernelEmission.HasWarmup"/>), each relay host in turn, outermost first; empty when the module
    /// context can name the class.
    /// </summary>
    public static EquatableArray<WarmupRelay> Create(in HostClassHeader header, IReadOnlyList<KernelEmission> kernels)
    {
        var hosts = header.WarmupRelayHosts;
        if (hosts.IsDefaultOrEmpty)
        {
            return default;
        }

        var firstOuterIndex = header.OuterDeclarations.Length - hosts.Length;
        var relays = ImmutableArray.CreateBuilder<WarmupRelay>();
        for (var level = 0; level < hosts.Length; level++)
        {
            var isLast = level == hosts.Length - 1;
            foreach (var kernel in kernels)
            {
                if (!kernel.HasWarmup)
                {
                    continue;
                }

                var name = kernel.Host.WarmupRelayName;
                relays.Add(isLast
                    ? new WarmupRelay(firstOuterIndex + level, name, header.FullyQualifiedName,
                        KernelHostNames.GetWarmupMethodName(kernel.MemberKey))
                    : new WarmupRelay(firstOuterIndex + level, name, hosts[level + 1], name));
            }
        }

        return relays.ToImmutable().AsEquatableArray();
    }
}

/// <summary>The kernels of one compilation: the class models, and the warmups in the order of the kernels.</summary>
internal readonly record struct KernelModuleModel(
    EquatableArray<KernelClassModel> Classes,
    EquatableArray<KernelWarmup> Warmups)
{
    /// <summary>Groups <paramref name="kernels"/> by class, in the order the classes first appear.</summary>
    public static KernelModuleModel Create(ImmutableArray<TranspiledKernelResult> kernels)
    {
        var kernelsByClass = new Dictionary<string, ImmutableArray<TranspiledKernelResult>.Builder>(StringComparer.Ordinal);
        var classIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var classOrder = ImmutableArray.CreateBuilder<HostClassHeader>();
        foreach (var kernel in kernels)
        {
            if (!kernelsByClass.TryGetValue(kernel.Class.HintName, out var group))
            {
                group = ImmutableArray.CreateBuilder<TranspiledKernelResult>();
                kernelsByClass[kernel.Class.HintName] = group;
                classIndices[kernel.Class.HintName] = classOrder.Count;
                classOrder.Add(kernel.Class);
            }

            group.Add(kernel);
        }

        var classes = ImmutableArray.CreateBuilder<KernelClassModel>(classOrder.Count);
        classOrder
            .AsStackEnumerable()
            .Select(kernelsByClass, static (header, groups) => KernelClassModel.Create(header, groups[header.HintName]))
            .AddTo(classes);
        var classModels = classes.MoveToImmutable();

        // The module context warms the kernels up in their own order, which may interleave the classes
        var positions = new Dictionary<string, int>(StringComparer.Ordinal);
        var warmups = ImmutableArray.CreateBuilder<KernelWarmup>(kernels.Length);
        foreach (var kernel in kernels)
        {
            var hintName = kernel.Class.HintName;
            positions.TryGetValue(hintName, out var position);
            positions[hintName] = position + 1;
            var emission = classModels[classIndices[hintName]].Kernels[position];
            if (emission.HasWarmup)
            {
                warmups.Add(KernelWarmup.Create(kernel.Class, emission));
            }
        }

        return new KernelModuleModel(classModels.AsEquatableArray(), warmups.ToImmutable().AsEquatableArray());
    }
}
