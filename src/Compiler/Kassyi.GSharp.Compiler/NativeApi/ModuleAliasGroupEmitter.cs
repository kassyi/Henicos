namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Writes the comparison of a module launcher's arrays and <c>ref</c> / <c>in</c> values for shared memory
/// (<c>HostAliasGroups</c> of the runtime) and the handling of the groups it finds, for
/// <see cref="ModuleKernelLaunchEmitter"/>: one array passed to two parameters, or one variable passed by reference
/// twice, is one memory in C# and gets one device region instead of a buffer each (#1061). The pins, ranges and the
/// comparison are those of the kernel launchers (<see cref="AliasRangeEmitter"/>).
/// </summary>
internal static class ModuleAliasGroupEmitter
{
    private const string Aliases = AliasRangeEmitter.Aliases;

    private const string Zero = HostTypeNames.IntPtr + ".Zero";

    /// <summary>The condition that alias candidate <paramref name="index"/> is a member of a group.</summary>
    public static string MemberTest(int index) => $"{Aliases}?.IsMember({index}) == true";

    /// <summary>The device address of member <paramref name="index"/> in its group's region.</summary>
    public static string DevicePointer(int index) => $"{Aliases}.GetDevicePointer({index})";

    // HostAliasGroups? __gsharp_aliases = null;
    /// <summary>Declares the groups, before the <c>try</c> block whose <c>finally</c> frees them.</summary>
    public static void EmitDeclaration(SourceWriter writer) =>
        writer.AppendLine($"{AliasRangeEmitter.GroupsType}? {Aliases} = null;");

    // __gsharp_aliases?.FreeDevice(<stream>);
    /// <summary>Frees the regions of the groups, in the launcher's <c>finally</c> block.</summary>
    public static void EmitFree(SourceWriter writer, ModuleKernelLaunchPlan plan) =>
        writer.AppendLine($"{Aliases}?.FreeDevice({plan.Launch.Stream ?? Zero});");

    // unsafe { <pins> { __gsharp_aliases = HostAliasGroups.Find(...);
    //   __gsharp_aliases?.CopyToDevice(<ranges>, <stream>); } }
    /// <summary>Finds the groups and fills their regions, inside the launcher's <c>try</c> block.</summary>
    public static void EmitGroups(SourceWriter writer, ModuleKernelLaunchPlan plan)
    {
        var ranges = plan.AliasRanges;
        writer.AppendLine("unsafe");
        using (writer.Scope())
        using (AliasRangeEmitter.WritePinnedRanges(writer, ranges))
        {
            AliasRangeEmitter.WriteFind(writer, ranges);
            var stream = plan.Launch.Stream ?? Zero;
            writer.AppendLine($"{Aliases}?.CopyToDevice({AliasRangeEmitter.RangesSpan(ranges)}, {stream});");
        }

        writer.AppendLine();
    }

    // if (__gsharp_aliases != null) { unsafe { <pins> { __gsharp_aliases.CopyToHost(<ranges>, <stream>); } } }
    /// <summary>Copies the groups back after the launch, pinning the arguments again.</summary>
    public static void EmitCopyBack(SourceWriter writer, ModuleKernelLaunchPlan plan)
    {
        if (!plan.ComparesAliases)
        {
            return;
        }

        var ranges = plan.AliasRanges;
        writer.AppendLine($"if ({Aliases} != null)");
        using (writer.Scope())
        {
            writer.AppendLine("unsafe");
            using (writer.Scope())
            using (AliasRangeEmitter.WritePinnedRanges(writer, ranges))
            {
                var stream = plan.Launch.Stream ?? Zero;
                writer.AppendLine($"{Aliases}.CopyToHost({AliasRangeEmitter.RangesSpan(ranges)}, {stream});");
            }
        }
    }

    // When the argument has no buffer: a null nullable array, or a member of an alias group, which uses the group's
    // region; null when it always has one
    internal static string? NoBufferCondition(ModuleKernelLaunchPlan plan, ModuleKernelArgument argument)
    {
        var index = IndexOf(plan, argument);
        var member = index >= 0 ? MemberTest(index) : null;
        var isNull = argument.AllowsNull ? $"{argument.Name.ToCSharpIdentifier()} is null" : null;
        return (isNull, member) switch
        {
            (null, null) => null,
            ({ } a, { } b) => $"{a} || {b}",
            _ => isNull ?? member
        };
    }

    // The number of <argument> among the alias candidates, or -1
    internal static int IndexOf(ModuleKernelLaunchPlan plan, ModuleKernelArgument argument) =>
        plan.ComparesAliases && argument.IsAliasCandidate
            ? plan.Arguments
                .AsStackEnumerable()
                .Count(argument.Ordinal, static (a, ordinal) => a.IsAliasCandidate && a.Ordinal < ordinal)
            : -1;
}
