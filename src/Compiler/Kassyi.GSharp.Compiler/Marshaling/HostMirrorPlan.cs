namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// The host mirror of a converted struct (<see cref="NativeStructEmitter.NeedsHostMirror"/>): its layout attribute and
/// fields, which give it the CLR layout of the struct, and the offset check of each field.
/// </summary>
/// <param name="LayoutKind">
/// The <c>LayoutKind</c> member: <c>Sequential</c>, <c>Explicit</c> or <c>Auto</c>, as the struct's.
/// </param>
/// <param name="Pack">The struct's <c>Pack</c>, or 0.</param>
/// <param name="Size">The struct's <c>Size</c>, or 0.</param>
/// <param name="Fields">The fields in <see cref="StructAbiMembers"/> order, which is the CLR order.</param>
internal readonly record struct HostMirrorPlan(
    string LayoutKind,
    int Pack,
    int Size,
    ImmutableArray<HostMirrorField> Fields)
{
    /// <summary>Whether any field's offset is checked.</summary>
    public bool HasOffsetCheck => Fields
        .AsStackEnumerable()
        .Any(static f => f.Check != HostMirrorOffsetCheck.None);

    /// <summary>
    /// Plans the mirror of <paramref name="s"/>, whose launcher is generated into <paramref name="launcherAssembly"/>.
    /// </summary>
    /// <remarks>
    /// A <c>LayoutKind.Explicit</c> struct gets an explicit mirror with the same <c>[FieldOffset]</c> values: a
    /// sequential mirror would place its fields in declaration order, which an explicit struct need not follow (#808).
    /// An explicit struct cannot hold a captured parameter or an auto-property backing field without an offset
    /// (CS0625), so every member has one. <c>CharSet</c> is not copied: it changes the marshaled layout, not the
    /// managed one that <c>Unsafe.As</c> reads.
    /// </remarks>
    public static HostMirrorPlan Create(INamedTypeSymbol s, StructLayoutInfo structLayout,
        IAssemblySymbol launcherAssembly)
    {
        var isExplicit = structLayout.Kind == TypeLayoutKind.Explicit;
        var fields = ImmutableArray.CreateBuilder<HostMirrorField>();
        var index = 0;
        foreach (var member in StructAbiMembers.Get(s))
        {
            int? fieldOffset = isExplicit ? StructLayoutReader.GetFieldOffset(member.StorageField) : null;
            int? predicted = fieldOffset ?? GetPredictedHostOffset(structLayout.Host, index);
            index++;
            fields.Add(new HostMirrorField(
                member,
                fieldOffset,
                GetCheck(member, predicted, launcherAssembly),
                predicted ?? 0));
        }

        var layoutKind = structLayout.Kind switch
        {
            TypeLayoutKind.Explicit => "Explicit",
            TypeLayoutKind.Auto => "Auto",
            _ => "Sequential"
        };
        StructLayoutReader.TryGetSize(s, out var size);
        return new HostMirrorPlan(layoutKind, structLayout.Pack, size, fields.ToImmutable());
    }

    // The host offset of the member at <index> (StructAbiMembers order, which HostLayout.Fields follows), or null when
    // the CLR decides the layout at run time
    private static int? GetPredictedHostOffset(HostLayout host, int index) =>
        host.IsKnown && index < host.Fields.Length ? host.Fields[index].Offset : null;

    private static HostMirrorOffsetCheck GetCheck(StructAbiMember member, int? predicted,
        IAssemblySymbol launcherAssembly)
    {
        if (member.Kind == StructAbiMemberKind.FixedBuffer || HostFieldAccess.IsPointer(member.Type))
        {
            return HostMirrorOffsetCheck.None;
        }

        // [WHY] A field the launcher can name is compared with the struct itself, which is what the CLR did; any other
        // field with the offset the compiler predicts, so that a struct whose fields are all private is checked too
        if (HostFieldAccess.CanCheckOffset(member, launcherAssembly))
        {
            return HostMirrorOffsetCheck.StructField;
        }

        return predicted is not null ? HostMirrorOffsetCheck.PredictedOffset : HostMirrorOffsetCheck.None;
    }
}
