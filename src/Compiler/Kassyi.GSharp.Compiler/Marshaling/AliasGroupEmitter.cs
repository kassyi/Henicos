namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Writes the comparison of a launch's arguments for overlapping memory (<see cref="LaunchAliasPlan"/>) and the
/// handling of the groups it finds: one device region per group on CUDA (#857, #1061), one copy for the
/// <c>[In]</c> members of a group on the CPU (#985). A launch without an overlap gets <see langword="null"/> from the
/// comparison and runs the launcher's code for each argument as before.
/// </summary>
internal static class AliasGroupEmitter
{
    private const string GroupsType = AliasRangeEmitter.GroupsType;
    private const string Aliases = AliasRangeEmitter.Aliases;
    private const string DeviceArrays = GSharpInternalNames.Launcher.DeviceArrays;
    private const string ImmutableCollectionsMarshalType =
        "global::System.Runtime.InteropServices.ImmutableCollectionsMarshal";
    private const string StagingType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasStaging";
    private const string CopiesType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasCopies";
    private const string Group = GSharpInternalNames.Launcher.AliasGroup;
    private const string Host = GSharpInternalNames.Launcher.AliasHost;
    private const string Native = GSharpInternalNames.Launcher.AliasNative;
    private const string Index = GSharpInternalNames.Launcher.AliasIndex;
    private const string Handles = GSharpInternalNames.Launcher.AliasHandles;

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
        using (AliasRangeEmitter.WritePinnedRanges(writer, plan.Ranges))
        {
            AliasRangeEmitter.WriteFind(writer, plan.Ranges);
            WriteClearedOutArguments(writer, sharing);
            writer.AppendLine($"if ({Aliases} != null)");
            using (writer.Scope())
            {
                writer.AppendLine($"{Aliases}.CopyToDevice({AliasRangeEmitter.RangesSpan(plan.Ranges)}, hStream);");
                for (var k = 0; k < plan.Candidates.Length && sharing.UsesTable; k++)
                {
                    var shape = sharing.Parameters[plan.Candidates[k]];
                    if (shape.IsSharableArray)
                    {
                        writer.AppendLine($"if ({Aliases}.IsMember({k})) " +
                            $"{DeviceArrays}.AddShared({shape.Identifier}, {Aliases}.GetDevicePointer({k}));");
                    }
                }

                WriteConversions(writer, plan, toDevice: true, isCuda: true);
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
            using (AliasRangeEmitter.WritePinnedRanges(writer, plan.Ranges))
            {
                writer.AppendLine($"{Aliases}.CopyToHost({AliasRangeEmitter.RangesSpan(plan.Ranges)}, hStream);");
                WriteConversions(writer, plan, toDevice: false, isCuda: true);
            }
        }
    }

    /// <summary>CUDA: frees the regions of the groups, in the launcher's <c>finally</c> block.</summary>
    public static void WriteCudaFree(SourceWriter writer, in LaunchAliasPlan plan) =>
        writer.AppendLineIf(plan.IsActive, $"{Aliases}?.FreeDevice(hStream);");

    // List<IntPtr>? __gsharp_alias_handles = null;   (CPU, before the try block that frees them)
    // [WHY] Created only with the groups (WriteCpuConversions), so that a launch without an overlap allocates nothing
    /// <summary>CPU: declares the list of the handles that the conversion of the groups pins.</summary>
    public static void WriteCpuHandlesDeclaration(SourceWriter writer, in LaunchAliasPlan plan) =>
        writer.AppendLineIf(plan.HasConversions, $"{HostTypeNames.PointerList}? {Handles} = null;");

    // if (__gsharp_alias_handles != null) foreach (IntPtr __gsharp_handle in ...) GCHandle.FromIntPtr(...).Free();
    /// <summary>CPU: frees the handles of <see cref="WriteCpuHandlesDeclaration"/>, in the finally block.</summary>
    public static void WriteCpuHandlesRelease(SourceWriter writer, in LaunchAliasPlan plan)
    {
        if (!plan.HasConversions)
        {
            return;
        }

        const string Handle = GSharpInternalNames.Launcher.PinnedHandle;
        writer.Append($"if ({Handles} != null) foreach ({HostTypeNames.IntPtr} {Handle} in {Handles}) ");
        writer.AppendLine($"{HostTypeNames.GCHandle}.FromIntPtr({Handle}).Free();");
    }

    // if (__gsharp_aliases != null) { <pins> { <conversion of the converted groups> } }
    /// <summary>
    /// CPU: converts the groups of converted elements into their buffers before the native call
    /// (<paramref name="toDevice"/>), or back into the host elements after it, pinning the arguments again.
    /// </summary>
    public static void WriteCpuConversions(SourceWriter writer, in LaunchAliasPlan plan, bool toDevice)
    {
        if (!plan.HasConversions)
        {
            return;
        }

        writer.AppendLine($"if ({Aliases} != null)");
        using (writer.Scope())
        {
            writer.AppendLineIf(toDevice, $"{Handles} = new();");
            using (AliasRangeEmitter.WritePinnedRanges(writer, plan.Ranges))
            {
                WriteConversions(writer, plan, toDevice, isCuda: false);
            }
        }
    }

    // CPU: HostAliasStaging.GetBufferPointer(__gsharp_aliases, <k>), the member's address in its group's buffer
    /// <summary>
    /// CPU: the condition and the address that a converted member of a group passes to the native kernel instead of
    /// its own shadow, or <see langword="null"/> when the parameter is no converted candidate.
    /// </summary>
    public static (string Member, string Pointer)? CpuBufferPointer(in LaunchAliasPlan plan, int parameterIndex)
    {
        if (!plan.IsConvertedMember(parameterIndex) || MemberTest(plan, parameterIndex) is not { } member)
        {
            return null;
        }

        var index = plan.MemberIndexOf(parameterIndex);
        return (member, $"{StagingType}.GetBufferPointer({Aliases}, {index})");
    }

    // for (int g = 0; g < __gsharp_aliases.GroupCount; g++)
    // { if (__gsharp_aliases.GetGroupLayout(g).ConversionKey != <key>) continue;
    //   Span<E> __gsharp_alias_host = HostAliasStaging.GetHostElements<E>(__gsharp_aliases, g, <ranges>);
    //   Span<S> __gsharp_alias_native = HostAliasStaging.GetBuffer<S>(__gsharp_aliases, g);
    //   to the device: if (<copies in>) { for (...) Marshal_<E>(ref host[i], ref native[i], <allocations>, hStream);
    //                  [HostAliasStaging.CopyToDevice(__gsharp_aliases, g, hStream);] }
    //   back:          if (<copies back>) { [HostAliasStaging.CopyToHost(__gsharp_aliases, g, hStream);]
    //                  for (...) Unmarshal_<E>(ref host[i], ref native[i], hStream); } }
    // [WHY] The members of a group of converted elements are one memory on the host; converting the group as a whole
    // into one buffer in the device layout lets each member address its elements in it, so a write through one member
    // is seen through the others. A launch without such a group never gets here (no groups)
    private static void WriteConversions(SourceWriter writer, in LaunchAliasPlan plan, bool toDevice, bool isCuda)
    {
        foreach (var conversion in plan.Conversions)
        {
            writer.AppendLine($"for (int {Group} = 0; {Group} < {Aliases}.GroupCount; {Group}++)");
            using (writer.Scope())
            {
                var layout = $"{Aliases}.GetGroupLayout({Group})";
                writer.AppendLine($"if ({layout}.ConversionKey != {conversion.Key}) continue;");
                writer.Append($"{HostTypeNames.Span}<{conversion.HostType}> {Host} = ");
                writer.AppendLine($"{StagingType}.GetHostElements<{conversion.HostType}>({Aliases}, {Group}, " +
                    $"{AliasRangeEmitter.RangesSpan(plan.Ranges)});");
                writer.Append($"{HostTypeNames.Span}<{conversion.ShadowType}> {Native} = ");
                writer.AppendLine($"{StagingType}.GetBuffer<{conversion.ShadowType}>({Aliases}, {Group});");
                writer.AppendLine($"if ({layout}.{(toDevice ? "CopiesIn" : "CopiesBack")})");
                using (writer.Scope())
                {
                    WriteConversionLoop(writer, conversion, toDevice, isCuda);
                }
            }
        }
    }

    private static void WriteConversionLoop(SourceWriter writer, in AliasConversion conversion, bool toDevice,
        bool isCuda)
    {
        writer.AppendLineIf(isCuda && !toDevice, $"{StagingType}.CopyToHost({Aliases}, {Group}, hStream);");
        writer.AppendLine($"for (int {Index} = 0; {Index} < {Host}.Length; {Index}++)");
        using (writer.Scope())
        {
            var elements = $"ref {Host}[{Index}], ref {Native}[{Index}]";
            writer.AppendLine(toDevice
                ? $"{conversion.MarshalMethod}({elements}, {conversion.Allocations}, hStream);"
                : $"{conversion.UnmarshalMethod}({elements}, hStream);");
        }

        writer.AppendLineIf(isCuda && toDevice, $"{StagingType}.CopyToDevice({Aliases}, {Group}, hStream);");
    }

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
        var scope = AliasRangeEmitter.WritePinnedRanges(writer, plan.Ranges);
        AliasRangeEmitter.WriteFind(writer, plan.Ranges);
        WriteClearedOutArguments(writer, sharing);
        return scope;
    }

    // if (!(__gsharp_aliases?.IsMember(<k>) == true)) <out name> = default;   for each out parameter that is compared
    // [WHY] An out argument that no other argument refers to is cleared, as it always was; one that does keeps the
    // caller's value, which C# reads through the ref or in parameter that refers to the same variable (#1841)
    private static void WriteClearedOutArguments(SourceWriter writer, in ArraySharingPlan sharing)
    {
        var plan = sharing.Aliases;
        for (var k = 0; k < plan.Candidates.Length; k++)
        {
            var shape = sharing.Parameters[plan.Candidates[k]];
            if (shape.RefKind == RefKind.Out && !shape.IsArray)
            {
                writer.AppendLine($"if (!({MemberTest(plan, plan.Candidates[k])})) {shape.Identifier} = default;");
            }
        }
    }

    // Span: if (HostAliasCopies.TryGetHostView<T>(__gsharp_aliases, <k>, <ranges>, out Span<T> copy))
    //     { <name> = copy; }
    // Memory: if (HostAliasCopies.TryGetHostMemory<T>(..., out Memory<T> copy[, <copies>, <CopyArrays_>]))
    //     { <name> = copy; }
    // Others: if (HostAliasCopies.TryGetHostCopy<T>(..., out T[]? copy, out int start[, ...]))
    //     { <name> = <over copy>; }
    // [WHY] A member of a group that is written back gets the caller's memory, as it did; the members of a group of
    // [In] arguments get one copy, so that a write through one is seen through the others, as on the device. A member
    // of another element type views the copy (bytes for a span, a MemoryManager for a Memory)
    /// <summary>CPU: the statement that gives member <paramref name="parameterIndex"/> its group's copy.</summary>
    public static void WriteCpuMemberCopy(SourceWriter writer, in ArraySharingPlan sharing, int parameterIndex)
    {
        var shape = sharing.Parameters[parameterIndex];
        var copy = KernelHostNames.GetLocal(LauncherLocal.AliasCopy, shape);
        var element = ConvertedArrayEmitter.GetElementTypeName(shape);
        var copyArrays = shape.Cpu.ConvertedElementCopyArraysMethodName;
        var view = shape.Container switch
        {
            ArrayContainerKind.Span or ArrayContainerKind.ReadOnlySpan when copyArrays is null => "Span",
            ArrayContainerKind.Memory or ArrayContainerKind.ReadOnlyMemory => "Memory",
            _ => null
        };
        if (view is null)
        {
            WriteCpuTypedCopy(writer, sharing, parameterIndex);
            return;
        }

        var method = view == "Span" ? "TryGetHostView" : "TryGetHostMemory";
        writer.Append($"if ({CopyCall(sharing, parameterIndex, method, element)}");
        writer.Append($"out global::System.{view}<{element}> {copy}");
        writer.AppendLine(view == "Memory" ? $"{ArraysArguments(copyArrays)}))" : "))");
        using (writer.Scope())
        {
            writer.AppendLine($"{shape.Identifier} = {copy};");
        }
    }

    // if (__gsharp_aliases != null)
    // { HostAliasCopies.TryGetHostCopy<T>(__gsharp_aliases, <k>, <ranges>, out _, out _); ... }
    // with one call per member that needs a typed array (T[], ArraySegment, ImmutableArray)
    // [WHY] The copy of a group takes the element type of its first request; the members that need a typed array ask
    // first, so that a span or Memory member of another element type views their copy instead of typing it
    /// <summary>CPU: makes the typed copies of the groups before the members take them.</summary>
    public static void WriteCpuTypedCopiesFirst(SourceWriter writer, in ArraySharingPlan sharing)
    {
        var plan = sharing.Aliases;
        if (!plan.IsActive || !plan.Candidates
                .AsStackEnumerable()
                .Any(sharing, static (parameterIndex, state) => NeedsTypedCopy(state, parameterIndex)))
        {
            return;
        }

        writer.AppendLine($"if ({Aliases} != null)");
        using (writer.Scope())
        {
            foreach (var parameterIndex in plan.Candidates)
            {
                if (NeedsTypedCopy(sharing, parameterIndex))
                {
                    var shape = sharing.Parameters[parameterIndex];
                    var call = CopyCall(sharing, parameterIndex, "TryGetHostCopy",
                        ConvertedArrayEmitter.GetElementTypeName(shape));
                    var arrays = ArraysArguments(shape.Cpu.ConvertedElementCopyArraysMethodName);
                    writer.AppendLine($"{call}out _, out _{arrays});");
                }
            }
        }
    }

    // A member that may take its group's copy and needs a typed array for it
    private static bool NeedsTypedCopy(in ArraySharingPlan sharing, int parameterIndex) =>
        sharing.Parameters[parameterIndex] is { IsArray: true } shape &&
        sharing.Aliases.MayTakeGroupCopy(parameterIndex) &&
        shape.Container is ArrayContainerKind.Array or ArrayContainerKind.ArraySegment
            or ArrayContainerKind.ImmutableArray;

    // if (HostAliasCopies.TryGetHostCopy<T>(..., out T[]? copy, out int start[, ...]))
    // { <name> = <container over copy>; }
    private static void WriteCpuTypedCopy(SourceWriter writer, in ArraySharingPlan sharing, int parameterIndex)
    {
        var shape = sharing.Parameters[parameterIndex];
        var copy = KernelHostNames.GetLocal(LauncherLocal.AliasCopy, shape);
        var start = KernelHostNames.GetLocal(LauncherLocal.AliasStart, shape);
        var element = ConvertedArrayEmitter.GetElementTypeName(shape);
        var arrays = ArraysArguments(shape.Cpu.ConvertedElementCopyArraysMethodName);
        writer.Append($"if ({CopyCall(sharing, parameterIndex, "TryGetHostCopy", element)}");
        writer.AppendLine($"out {element}[]? {copy}, out int {start}{arrays}))");
        using (writer.Scope())
        {
            var length = shape.LengthExpression;
            writer.AppendLine(shape.Container switch
            {
                ArrayContainerKind.Array => $"{shape.Identifier} = {copy};",
                ArrayContainerKind.Span or ArrayContainerKind.ReadOnlySpan =>
                    $"{shape.Identifier} = {copy}.AsSpan({start}, {length});",
                ArrayContainerKind.ArraySegment =>
                    $"{shape.Identifier} = new global::System.ArraySegment<{element}>({copy}, {start}, {length});",
                _ => $"{shape.Identifier} = {ImmutableCollectionsMarshalType}.AsImmutableArray({copy});"
            });
        }
    }

    // HostAliasCopies.<method><<T>>(__gsharp_aliases, <k>, <ranges>,   (the out arguments follow)
    private static string CopyCall(in ArraySharingPlan sharing, int parameterIndex, string method, string element) =>
        $"{CopiesType}.{method}<{element}>({Aliases}, {sharing.Aliases.MemberIndexOf(parameterIndex)}, " +
        $"{AliasRangeEmitter.RangesSpan(sharing.Aliases.Ranges)}, ";

    // ", <copies>, <CopyArrays_>": an element that holds arrays gets copies of them too, through the launch's
    // HostArrayCopies; empty for other elements
    private static string ArraysArguments(string? copyArrays) =>
        copyArrays is null ? "" : $", {GSharpInternalNames.Launcher.ArrayCopies}, {copyArrays}";
}
