using Kassyi.GSharp.Compiler.Marshaling;

namespace Kassyi.GSharp.Compiler.Layout;

/// <summary>
/// The launch-time layout checks of the block-copied user structs that a launch's transfer plans reach, the same
/// checks as <see cref="KernelHostModel"/> collects from the symbols (<see cref="StructLayoutGuard"/>).
/// </summary>
/// <remarks>Nothing calls it yet (#976); the launch emitters move onto it with the plans.</remarks>
internal static class TransferPlanGuards
{
    /// <summary>
    /// The checks for the plans of a launch's parameters and return value, in that order, each struct once, as seen
    /// from a launcher in the assembly <paramref name="launcherAssemblyName"/>.
    /// </summary>
    public static EquatableArray<StructLayoutGuard> Collect(ImmutableArray<TransferPlan> plans,
        string launcherAssemblyName)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var guards = ImmutableArray.CreateBuilder<StructLayoutGuard>();
        foreach (var plan in plans)
        {
            Collect(plan, launcherAssemblyName, visited, guards);
        }

        return guards.ToImmutable().AsEquatableArray();
    }

    // [WHY] Like KernelHostModel.CollectBlockCopiedStructs, an array is looked into once (its element, not the element
    // of a nested array), a Nullable<T> and a struct outside the user structs are not looked into, and every member of
    // a user struct is, whether it is copied as a block or converted
    private static void Collect(TransferPlan plan, string launcherAssemblyName, HashSet<string> visited,
        ImmutableArray<StructLayoutGuard>.Builder guards)
    {
        switch (plan)
        {
            case TransferPlan.ArrayView { Element: not TransferPlan.ArrayView } array:
                Collect(array.Element, launcherAssemblyName, visited, guards);
                break;
            case TransferPlan.Blit { UserStructDisplayName: { } displayName } blit:
                if (!visited.Add(blit.CSharpType))
                {
                    return;
                }

                var fields = ImmutableArray.CreateBuilder<FieldOffsetGuard>();
                blit.Members
                    .AsStackEnumerable()
                    .Where(launcherAssemblyName, static (m, assembly) => m.IsNameableFrom(assembly))
                    .Select(static m => new FieldOffsetGuard(m.Name.ToCSharpIdentifier(), m.FullyQualifiedTypeName, m.HostOffset ?? 0))
                    .AddTo(fields);
                var guard = new StructLayoutGuard(
                    blit.CSharpType,
                    displayName,
                    blit.Size,
                    fields.ToImmutable().AsEquatableArray()
                );
                guards.Add(guard);
                CollectMembers(blit.Members, launcherAssemblyName, visited, guards);
                break;
            case TransferPlan.Convert convert:
                if (visited.Add(convert.CSharpType))
                {
                    CollectMembers(convert.Members, launcherAssemblyName, visited, guards);
                }

                break;
        }
    }

    private static void CollectMembers(EquatableArray<MemberPlan> members, string launcherAssemblyName,
        HashSet<string> visited, ImmutableArray<StructLayoutGuard>.Builder guards)
    {
        foreach (var member in members)
        {
            Collect(member.Plan, launcherAssemblyName, visited, guards);
        }
    }
}
