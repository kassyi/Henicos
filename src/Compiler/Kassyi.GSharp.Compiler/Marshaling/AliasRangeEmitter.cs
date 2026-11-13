namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Writes the comparison that every launcher family shares (<see cref="AliasGroupEmitter"/> for the kernel launchers,
/// <c>ModuleAliasGroupEmitter</c> for the module launchers): the pins and the range list of the candidate arguments
/// (<see cref="AliasRange"/>) and the call of <c>HostAliasGroups.Find</c>.
/// </summary>
internal static class AliasRangeEmitter
{
    /// <summary>The runtime type that finds and holds the groups.</summary>
    public const string GroupsType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasGroups";

    /// <summary>The launcher local that holds the groups, <see langword="null"/> without an overlap.</summary>
    public const string Aliases = GSharpInternalNames.Launcher.Aliases;

    private const string PinsType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasPins";
    private const string Ranges = GSharpInternalNames.Launcher.AliasRanges;
    private const int RangeFields = 6;

    // fixed (byte* <pin a> = &HostAliasPins.<pin call a>, <pin b> = &HostAliasPins.<pin call b>, ...)
    // { nint* __gsharp_alias_ranges = stackalloc nint[<6n>];
    //   __gsharp_alias_ranges[<6k>] = (nint)<pin>; [<6k + 1>] = <byte length>; [<6k + 2>] = sizeof(<T>);
    //   [<6k + 3>] = sizeof(<device T>); [<6k + 4>] = <device alignment>; [<6k + 5>] = <conversion>; ...
    // [WHY] Pinned, every address is read at one time, so two arguments overlap exactly when their ranges do; a group
    // lies in one object, so the offsets stay valid after the arguments are unpinned
    /// <summary>Pins the candidates and fills the range list; returns the <c>fixed</c> statement's scope.</summary>
    public static SourceWriterScope WritePinnedRanges(SourceWriter writer, ImmutableArray<AliasRange> ranges)
    {
        writer.Append("fixed (byte* ");
        for (var k = 0; k < ranges.Length; k++)
        {
            writer.AppendIf(k > 0, ", ");
            writer.Append($"{ranges[k].Pinned} = &{PinsType}.{ranges[k].PinCall}");
        }

        writer.AppendLine(")");
        var scope = writer.Scope();
        writer.AppendLine($"nint* {Ranges} = stackalloc nint[{ranges.Length * RangeFields}];");
        for (var k = 0; k < ranges.Length; k++)
        {
            var at = k * RangeFields;
            writer.AppendLine($"{Ranges}[{at}] = (nint){ranges[k].Pinned};");
            writer.AppendLine($"{Ranges}[{at + 1}] = {ranges[k].ByteLength};");
            writer.AppendLine($"{Ranges}[{at + 2}] = sizeof({ranges[k].ElementType});");
            writer.AppendLine($"{Ranges}[{at + 3}] = sizeof({ranges[k].DeviceElementType});");
            writer.AppendLine($"{Ranges}[{at + 4}] = {ranges[k].Alignment};");
            writer.AppendLine($"{Ranges}[{at + 5}] = {ranges[k].ConversionKey};");
        }

        return scope;
    }

    // __gsharp_aliases = HostAliasGroups.Find(<ranges>, new byte[] { <traits> }, "<names>");
    /// <summary>Writes the call that finds the groups, inside <see cref="WritePinnedRanges"/>.</summary>
    public static void WriteFind(SourceWriter writer, ImmutableArray<AliasRange> ranges)
    {
        writer.Append($"{Aliases} = {GroupsType}.Find({RangesSpan(ranges)}, new byte[] ");
        using (writer.Braces())
        {
            for (var k = 0; k < ranges.Length; k++)
            {
                writer.AppendIf(k > 0, ", ");
                writer.Append(ranges[k].Traits.ToString(CultureInfo.InvariantCulture));
            }
        }

        writer.Append(", \"");
        for (var k = 0; k < ranges.Length; k++)
        {
            writer.AppendIf(k > 0, ",");
            writer.Append(ranges[k].Name);
        }

        writer.AppendLine("\");");
    }

    /// <summary>The range list as the span that the <c>HostAliasGroups</c> methods take.</summary>
    public static string RangesSpan(ImmutableArray<AliasRange> ranges) =>
        $"new global::System.ReadOnlySpan<nint>({Ranges}, {ranges.Length * RangeFields})";
}
