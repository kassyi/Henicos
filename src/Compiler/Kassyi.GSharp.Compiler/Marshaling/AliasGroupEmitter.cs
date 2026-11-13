namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Writes the comparison of a launch's arguments for overlapping memory (<see cref="LaunchAliasPlan"/>) and the
/// handling of the groups it finds: one device region per group on CUDA (#857, #1061), one copy for the
/// <c>[In]</c> members of a group on the CPU (#985). A launch without an overlap gets <see langword="null"/> from the
/// comparison and runs the launcher's code for each argument as before.
/// </summary>
internal static class AliasGroupEmitter
{
    private const string GroupsType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasGroups";
    private const string PinsType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasPins";
    private const string Aliases = GSharpInternalNames.Launcher.Aliases;
    private const string Ranges = GSharpInternalNames.Launcher.AliasRanges;
    private const string DeviceArrays = GSharpInternalNames.Launcher.DeviceArrays;
    private const int RangeFields = 3;
    private const string ImmutableCollectionsMarshalType =
        "global::System.Runtime.InteropServices.ImmutableCollectionsMarshal";

    // HostAliasGroups? __gsharp_aliases = null;
    /// <summary>Declares the groups of the launch, before the launcher's <c>try</c> block.</summary>
    public static void WriteDeclaration(SourceWriter writer, in LaunchAliasPlan plan) =>
        writer.AppendLineIf(plan.IsActive, $"{GroupsType}? {Aliases} = null;");

    /// <summary>
    /// The condition that parameter <paramref name="parameterIndex"/> is a member of a group, or <see langword="null"/>
    /// when it never takes its copy from a group.
    /// </summary>
    public static string? MemberTest(in LaunchAliasPlan plan, int parameterIndex) =>
        plan.MemberIndexOf(parameterIndex) is var index and >= 0 ? $"{Aliases}?.IsMember({index}) == true" : null;

    // <pins> { __gsharp_aliases = HostAliasGroups.Find(...);
    //   if (__gsharp_aliases != null) { __gsharp_aliases.CopyToDevice(<ranges>, hStream); [<table>] } }
    /// <summary>
    /// CUDA: finds the groups, allocates and fills their device regions, and records the region of each <c>T[]</c>
    /// member in the launch's <c>DeviceArrayRegions</c>, so that a struct field that holds the same array takes it.
    /// </summary>
    public static void WriteCudaGroups(SourceWriter writer, in ArraySharingPlan sharing)
    {
        var plan = sharing.Aliases;
        if (!plan.IsActive)
        {
            return;
        }

        writer.AppendLine("// Find the arguments that overlap and give each group one device region");
        using (WritePinnedRanges(writer, plan, sharing.Parameters))
        {
            WriteFind(writer, plan, sharing.Parameters);
            writer.AppendLine($"if ({Aliases} != null)");
            using (writer.Scope())
            {
                writer.AppendLine($"{Aliases}.CopyToDevice({RangesSpan(plan)}, hStream);");
                if (!sharing.UsesTable)
                {
                    return;
                }

                for (var k = 0; k < plan.Candidates.Length; k++)
                {
                    var shape = sharing.Parameters[plan.Candidates[k]];
                    if (shape.IsSharableArray && plan.MemberIndexOf(plan.Candidates[k]) >= 0)
                    {
                        writer.AppendLine($"if ({Aliases}.IsMember({k})) " +
                            $"{DeviceArrays}.AddShared({shape.Identifier}, {Aliases}.GetDevicePointer({k}));");
                    }
                }
            }
        }
    }

    // if (__gsharp_aliases != null) { <pins> { __gsharp_aliases.CopyToHost(<ranges>, hStream); } }
    /// <summary>CUDA: copies the groups back after the launch, pinning the arguments again.</summary>
    public static void WriteCudaCopyBack(SourceWriter writer, in ArraySharingPlan sharing)
    {
        var plan = sharing.Aliases;
        if (!plan.IsActive)
        {
            return;
        }

        writer.AppendLine($"if ({Aliases} != null)");
        using (writer.Scope())
        {
            using (WritePinnedRanges(writer, plan, sharing.Parameters))
            {
                writer.AppendLine($"{Aliases}.CopyToHost({RangesSpan(plan)}, hStream);");
            }
        }
    }

    /// <summary>CUDA: frees the regions of the groups, in the launcher's <c>finally</c> block.</summary>
    public static void WriteCudaFree(SourceWriter writer, in LaunchAliasPlan plan) =>
        writer.AppendLineIf(plan.IsActive, $"{Aliases}?.FreeDevice(hStream);");

    /// <summary>The device address of member <paramref name="parameterIndex"/>.</summary>
    public static string DevicePointer(in LaunchAliasPlan plan, int parameterIndex) =>
        $"{Aliases}.GetDevicePointer({plan.MemberIndexOf(parameterIndex)})";

    // <pins> { __gsharp_aliases = HostAliasGroups.Find(...); <copies> }
    /// <summary>
    /// CPU: finds the groups and opens the scope that keeps the arguments pinned while the launcher makes its copies
    /// (<see cref="WriteCpuMemberCopy"/>), or returns an empty scope when the launcher compares nothing.
    /// </summary>
    public static SourceWriterScope WriteCpuGroups(SourceWriter writer, in ArraySharingPlan sharing)
    {
        var plan = sharing.Aliases;
        if (!plan.IsActive)
        {
            return default;
        }

        writer.AppendLine("// Find the arguments that overlap: the [In] members of a group share one copy");
        WriteDeclaration(writer, plan);
        var scope = WritePinnedRanges(writer, plan, sharing.Parameters);
        WriteFind(writer, plan, sharing.Parameters);
        return scope;
    }

    // if (__gsharp_aliases.TryGetHostCopy<T>(<k>, <ranges>, out var copy, out var start))
    // { <name> = <container over the copy>; }
    // [WHY] A member of a group that is written back gets the caller's memory, as it did; the members of a group of
    // [In] arguments get one copy, so that a write through one is seen through the others, as on the device
    /// <summary>CPU: the statement that gives member <paramref name="parameterIndex"/> its group's copy.</summary>
    public static void WriteCpuMemberCopy(SourceWriter writer, in ArraySharingPlan sharing, int parameterIndex)
    {
        var plan = sharing.Aliases;
        var shape = sharing.Parameters[parameterIndex];
        var copy = KernelHostNames.GetLocal(LauncherLocal.AliasCopy, shape);
        var start = KernelHostNames.GetLocal(LauncherLocal.AliasStart, shape);
        var element = ConvertedArrayEmitter.GetElementTypeName(shape);
        writer.Append($"if ({Aliases}.TryGetHostCopy<{element}>({plan.MemberIndexOf(parameterIndex)}, ");
        writer.AppendLine($"{RangesSpan(plan)}, out {element}[]? {copy}, out int {start}))");
        using (writer.Scope())
        {
            var length = shape.LengthExpression;
            writer.AppendLine(shape.Container switch
            {
                ArrayContainerKind.Array => $"{shape.Identifier} = {copy};",
                ArrayContainerKind.Span or ArrayContainerKind.ReadOnlySpan =>
                    $"{shape.Identifier} = {copy}.AsSpan({start}, {length});",
                ArrayContainerKind.Memory =>
                    $"{shape.Identifier} = new global::System.Memory<{element}>({copy}, {start}, {length});",
                ArrayContainerKind.ReadOnlyMemory =>
                    $"{shape.Identifier} = new global::System.ReadOnlyMemory<{element}>({copy}, {start}, {length});",
                ArrayContainerKind.ArraySegment =>
                    $"{shape.Identifier} = new global::System.ArraySegment<{element}>({copy}, {start}, {length});",
                _ => $"{shape.Identifier} = {ImmutableCollectionsMarshalType}.AsImmutableArray({copy});"
            });
        }
    }

    // fixed (byte* __gsharp_alias_pin_<a> = &HostAliasPins.Start(<span>),
    //        __gsharp_alias_pin_<b> = &HostAliasPins.StartOf(ref <name>), ...)
    // { nint* __gsharp_alias_ranges = stackalloc nint[<3n>];
    //   __gsharp_alias_ranges[<3k>] = (nint)<pin>; [<3k + 1>] = (nint)<length> * sizeof(<T>);
    //   [<3k + 2>] = sizeof(<T>); ...
    // [WHY] Pinned, every address is read at one time, so two arguments overlap exactly when their ranges do; a group
    // lies in one object, so the offsets stay valid after the arguments are unpinned
    private static SourceWriterScope WritePinnedRanges(SourceWriter writer, LaunchAliasPlan plan,
        EquatableArray<KernelParameterShape> parameters)
    {
        writer.Append("fixed (byte* ");
        for (var k = 0; k < plan.Candidates.Length; k++)
        {
            var shape = parameters[plan.Candidates[k]];
            writer.AppendIf(k > 0, ", ");
            writer.Append($"{KernelHostNames.GetLocal(LauncherLocal.AliasPinned, shape)} = &{PinsType}.");
            writer.Append(shape.IsArray ? $"Start({shape.SpanExpression})" : $"StartOf(ref {shape.Identifier})");
        }

        writer.AppendLine(")");
        var scope = writer.Scope();
        writer.AppendLine($"nint* {Ranges} = stackalloc nint[{plan.Candidates.Length * RangeFields}];");
        for (var k = 0; k < plan.Candidates.Length; k++)
        {
            var shape = parameters[plan.Candidates[k]];
            var at = k * RangeFields;
            var element = shape.IsArray ? ConvertedArrayEmitter.GetElementTypeName(shape) : shape.TypeName;
            var length = shape.IsArray ? $"(nint){shape.LengthExpression} * sizeof({element})" : $"sizeof({element})";
            writer.AppendLine($"{Ranges}[{at}] = (nint){KernelHostNames.GetLocal(LauncherLocal.AliasPinned, shape)};");
            writer.AppendLine($"{Ranges}[{at + 1}] = {length};");
            writer.AppendLine($"{Ranges}[{at + 2}] = sizeof({element});");
        }

        return scope;
    }

    // __gsharp_aliases = HostAliasGroups.Find(<ranges>, new byte[] { <traits> }, "<names>");
    private static void WriteFind(SourceWriter writer, in LaunchAliasPlan plan,
        EquatableArray<KernelParameterShape> parameters)
    {
        writer.Append($"{Aliases} = {GroupsType}.Find({RangesSpan(plan)}, new byte[] ");
        using (writer.Braces())
        {
            for (var k = 0; k < plan.Traits.Length; k++)
            {
                writer.AppendIf(k > 0, ", ");
                writer.Append(plan.Traits[k].ToString(CultureInfo.InvariantCulture));
            }
        }

        writer.Append(", \"");
        for (var k = 0; k < plan.Candidates.Length; k++)
        {
            writer.AppendIf(k > 0, ",");
            writer.Append(parameters[plan.Candidates[k]].Name);
        }

        writer.AppendLine("\");");
    }

    private static string RangesSpan(in LaunchAliasPlan plan) =>
        $"new global::System.ReadOnlySpan<nint>({Ranges}, {plan.Candidates.Length * RangeFields})";
}
