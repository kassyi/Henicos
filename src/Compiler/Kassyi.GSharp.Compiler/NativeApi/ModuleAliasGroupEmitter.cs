namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Writes the comparison of a module launcher's arrays and <c>ref</c> / <c>in</c> values for shared memory
/// (<c>HostAliasGroups</c> of the runtime) and the handling of the groups it finds, for
/// <see cref="ModuleKernelLaunchEmitter"/>: one array passed to two parameters, or one variable passed by reference
/// twice, is one memory in C# and gets one device region instead of a buffer each (#1061).
/// </summary>
internal static class ModuleAliasGroupEmitter
{
    private const string GroupsType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasGroups";
    private const string PinsType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasPins";

    private const string Aliases = GSharpInternalNames.Launcher.Aliases;

    private const string Ranges = GSharpInternalNames.Launcher.AliasRanges;

    private const string Zero = HostTypeNames.IntPtr + ".Zero";

    /// <summary>The condition that alias candidate <paramref name="index"/> is a member of a group.</summary>
    public static string MemberTest(int index) => $"{Aliases}?.IsMember({index}) == true";

    /// <summary>The device address of member <paramref name="index"/> in its group's region.</summary>
    public static string DevicePointer(int index) => $"{Aliases}.GetDevicePointer({index})";

    // __gsharp_aliases?.FreeDevice(<stream>);
    /// <summary>Frees the regions of the groups, in the launcher's <c>finally</c> block.</summary>
    public static void EmitFree(SourceWriter writer, ModuleKernelLaunchPlan plan) =>
        writer.AppendLine($"{Aliases}?.FreeDevice({plan.Launch.Stream ?? Zero});");

    // HostAliasGroups? __gsharp_aliases;
    // unsafe { <pins> { __gsharp_aliases = HostAliasGroups.Find(...);
    //   __gsharp_aliases?.CopyToDevice(<ranges>, <stream>); } }
    // [WHY] In C# one array passed to two parameters, or a variable passed by ref twice, is one memory (#1061): the
    // arguments that overlap get one device region instead of a buffer each, and their buffers stay unused
    internal static void EmitGroups(SourceWriter writer, ModuleKernelLaunchPlan plan)
    {
        writer.AppendLine($"{GroupsType}? {Aliases};");
        writer.AppendLine("unsafe");
        using (writer.Scope())
        using (EmitPinnedRanges(writer, plan))
        {
            writer.Append($"{Aliases} = {GroupsType}.Find({RangesSpan(plan)}, new byte[] ");
            using (writer.Braces())
            {
                var first = true;
                foreach (var argument in plan.Arguments)
                {
                    if (argument.IsAliasCandidate)
                    {
                        writer.AppendIf(!first, ", ");
                        writer.Append(argument.AliasTraits.ToString(CultureInfo.InvariantCulture));
                        first = false;
                    }
                }
            }

            writer.Append(", \"");
            var firstName = true;
            foreach (var argument in plan.Arguments)
            {
                if (argument.IsAliasCandidate)
                {
                    writer.AppendIf(!firstName, ",");
                    writer.Append(argument.Name);
                    firstName = false;
                }
            }

            writer.AppendLine("\");");
            writer.AppendLine($"{Aliases}?.CopyToDevice({RangesSpan(plan)}, {plan.Launch.Stream ?? Zero});");
        }

        writer.AppendLine();
    }

    // if (__gsharp_aliases != null) { unsafe { <pins> { __gsharp_aliases.CopyToHost(<ranges>, <stream>); } } }
    internal static void EmitCopyBack(SourceWriter writer, ModuleKernelLaunchPlan plan)
    {
        if (!plan.ComparesAliases)
        {
            return;
        }

        writer.AppendLine($"if ({Aliases} != null)");
        using (writer.Scope())
        {
            writer.AppendLine("unsafe");
            using (writer.Scope())
            using (EmitPinnedRanges(writer, plan))
            {
                writer.AppendLine($"{Aliases}.CopyToHost({RangesSpan(plan)}, {plan.Launch.Stream ?? Zero});");
            }
        }
    }

    // fixed (byte* __gsharp_alias_pin_<o> = &HostAliasPins.Start(<array>), ...
    //        = &HostAliasPins.StartOf(ref Unsafe.AsRef(in <value>)))
    // { nint* __gsharp_alias_ranges = stackalloc nint[<3n>]; <address, byte length, element size of each> }
    private static SourceWriterScope EmitPinnedRanges(SourceWriter writer, ModuleKernelLaunchPlan plan)
    {
        writer.Append("fixed (byte* ");
        var first = true;
        foreach (var argument in plan.Arguments)
        {
            if (!argument.IsAliasCandidate)
            {
                continue;
            }

            writer.AppendIf(!first, ", ");
            first = false;
            var pin = KernelHostNames.GetLocal(LauncherLocal.AliasPinned, argument.Ordinal);
            var name = ModuleKernelLaunchEmitter.Identifier(argument);
            writer.Append($"{pin} = &{PinsType}.");
            writer.Append(argument.IsArray
                ? $"Start({name})"
                : $"StartOf(ref {HostTypeNames.Unsafe}.AsRef(in {name}))");
        }

        writer.AppendLine(")");
        var scope = writer.Scope();
        writer.AppendLine($"nint* {Ranges} = stackalloc nint[{CandidateCount(plan) * 3}];");
        var at = 0;
        foreach (var argument in plan.Arguments)
        {
            if (!argument.IsAliasCandidate)
            {
                continue;
            }

            var name = ModuleKernelLaunchEmitter.Identifier(argument);
            var count = argument.AllowsNull ? $"({name}?.Length ?? 0)" : $"{name}.Length";
            var length = argument.IsArray
                ? $"(nint){count} * sizeof({argument.TypeName})"
                : $"sizeof({argument.TypeName})";
            var pin = KernelHostNames.GetLocal(LauncherLocal.AliasPinned, argument.Ordinal);
            writer.AppendLine($"{Ranges}[{at}] = (nint){pin};");
            writer.AppendLine($"{Ranges}[{at + 1}] = {length};");
            writer.AppendLine($"{Ranges}[{at + 2}] = sizeof({argument.TypeName});");
            at += 3;
        }

        return scope;
    }

    private static int CandidateCount(ModuleKernelLaunchPlan plan) =>
        plan.Arguments
            .AsStackEnumerable()
            .Count(static a => a.IsAliasCandidate);

    // The number of <argument> among the alias candidates, or -1
    internal static int IndexOf(ModuleKernelLaunchPlan plan, ModuleKernelArgument argument) =>
        plan.ComparesAliases && argument.IsAliasCandidate
            ? plan.Arguments
                .AsStackEnumerable()
                .Count(argument.Ordinal, static (a, ordinal) => a.IsAliasCandidate && a.Ordinal < ordinal)
            : -1;

    private static string RangesSpan(ModuleKernelLaunchPlan plan) =>
        $"new {HostTypeNames.ReadOnlySpan}<nint>({Ranges}, {CandidateCount(plan) * 3})";
}
