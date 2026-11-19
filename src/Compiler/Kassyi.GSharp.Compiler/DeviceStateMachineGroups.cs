using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Groups the managed sources of the device functions by the partial class that declares them: the state machines and
/// versions of the managed fallback, and the managed stack forms (<see cref="ManagedStackPipeline"/>).
/// </summary>
internal static class DeviceStateMachineGroups
{
    /// <summary>
    /// One group per class that has a managed source, in the order the classes first appear; keyed by the hint name,
    /// which identifies the class (#472).
    /// </summary>
    public static ImmutableArray<DeviceStateMachineGroup> Create(
        ImmutableArray<TranspiledDeviceMethodResult>.Builder distinctDeviceMethods,
        EquatableArray<ManagedStackDeviceForm> stackForms)
    {
        var sourcesByClass = new Dictionary<string, ImmutableArray<string>.Builder>(StringComparer.Ordinal);
        var stackSourcesByClass = new Dictionary<string, ImmutableArray<string>.Builder>(StringComparer.Ordinal);
        var classOrder = ImmutableArray.CreateBuilder<HostClassHeader>();

        // The class's lists, created when the class first has a source
        (ImmutableArray<string>.Builder Sources, ImmutableArray<string>.Builder StackSources) GetLists(
            HostClassHeader header)
        {
            if (!sourcesByClass.TryGetValue(header.HintName, out var sources))
            {
                sources = ImmutableArray.CreateBuilder<string>();
                sourcesByClass[header.HintName] = sources;
                stackSourcesByClass[header.HintName] = ImmutableArray.CreateBuilder<string>();
                classOrder.Add(header);
            }

            return (sources, stackSourcesByClass[header.HintName]);
        }

        foreach (var method in distinctDeviceMethods)
        {
            if (method.ManagedCSharpSource.Length > 0)
            {
                GetLists(method.Class).Sources.Add(method.ManagedCSharpSource);
            }
        }

        foreach (var form in stackForms)
        {
            GetLists(form.Class).StackSources.Add(form.Source);
        }

        var groups = ImmutableArray.CreateBuilder<DeviceStateMachineGroup>(classOrder.Count);
        classOrder
            .AsStackEnumerable()
            .Select((sourcesByClass, stackSourcesByClass), static (header, state) =>
                new DeviceStateMachineGroup(
                    header,
                    state.sourcesByClass[header.HintName].ToImmutable().AsEquatableArray(),
                    state.stackSourcesByClass[header.HintName].ToImmutable().AsEquatableArray()))
            .AddTo(groups);
        return groups.MoveToImmutable();
    }
}
