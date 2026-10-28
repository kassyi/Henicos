namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits explicit layout shadow structs representing C# structs in native device memory.</summary>
internal static class NativeStructEmitter
{
    /// <summary>Emits the explicit struct declaration with computed field offsets for <paramref name="dialect"/>.</summary>
    internal static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s, ITranspilerDialect dialect) =>
        WriteNativeStructDeclaration(writer, s, TypeLayoutCalculator.GetStructLayout(s, dialect));

    /// <summary>Emits the explicit struct declaration with computed field offsets from <paramref name="structLayout"/>.</summary>
    internal static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout)
    {
        var parents = AotHostNames.GetEnclosingCustomStructs(s);

        foreach (var p in parents)
        {
            writer.AppendLine($"public unsafe partial struct {AotHostNames.GetNativeStructName(p)}");
            writer.AppendLine("{");
        }

        writer.AppendLine(
            $"[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = {structLayout.Device.Size})]");
        writer.Append($"public unsafe partial struct {AotHostNames.GetNativeStructName(s)}");
        writer.AppendLine();

        using (writer.Scope())
        {
            foreach (var (field, offset, fieldLayout) in structLayout.DeviceFields)
            {
                // [WHY] A reference type other than an array has no device layout: GSH0078 reports it, and the shadow
                // leaves it out so that the generated code still compiles.
                if (IsUnrepresentableReference(field))
                {
                    continue;
                }

                writer.Append($"public const string DebugName_{field.Name} = \"");
                writer.AppendFullyQualifiedCsharpName(field.Type);
                writer.AppendLine("\";");
                writer.AppendLine($"[System.Runtime.InteropServices.FieldOffset({offset})]");

                MapToNativeField(writer, field, fieldLayout.Size);
            }

            if (NeedsHostMirror(s, structLayout))
            {
                WriteHostMirror(writer, s, structLayout);
            }
        }

        foreach (var dummy in parents)
        {
            writer.AppendLine("}");
        }

        writer.AppendLine();
    }

    /// <summary>The name of the host mirror nested in the shadow struct (<see cref="NeedsHostMirror"/>).</summary>
    internal const string HostMirrorName = "__gsharp_host_mirror";

    /// <summary>
    /// Whether the launcher reads and writes <paramref name="s"/> through a host mirror: a struct that it converts
    /// member by member and that holds the hidden field of a captured primary constructor parameter (<c>&lt;a&gt;P</c>),
    /// which generated C# cannot name (#495).
    /// </summary>
    /// <remarks>
    /// The mirror declares the fields of <paramref name="s"/> in <see cref="StructAbiMembers"/> order, which is the CLR
    /// order, with the same types, layout kind and <c>Pack</c>, so it has the CLR layout of <paramref name="s"/>; the hidden
    /// fields are named after their parameters. The marshaling reinterprets the host value as the mirror
    /// (<c>Unsafe.As</c>) after checking that both have the same size.
    /// </remarks>
    internal static bool NeedsHostMirror(INamedTypeSymbol s, StructLayoutInfo structLayout)
    {
        if (structLayout.Transfer == TransferClass.Identical)
        {
            return false;
        }

        foreach (var member in StructAbiMembers.Get(s))
        {
            if (member.Kind == StructAbiMemberKind.CapturedParameter)
            {
                return true;
            }
        }

        return false;
    }

    // [StructLayout(LayoutKind.<Sequential | Auto>[, Pack = <n>][, Size = <n>])]
    // public unsafe struct __gsharp_host_mirror {
    // public <type> <name>; ... public fixed <element> <name>[<n>];
    // public static readonly bool FieldOffsetsMatch = CheckFieldOffsets();
    // private static bool CheckFieldOffsets() { <S> s = default; ref var m = ref Unsafe.As<S, mirror>(ref s);
    // return <offset of s.F> == <offset of m.F> && ...; } }
    private static void WriteHostMirror(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout)
    {
        // [WHY] LayoutKind.Explicit cannot hold a captured parameter: its hidden field has no [FieldOffset] (CS0625)
        var kind = structLayout.Kind == TypeLayoutKind.Auto ? "Auto" : "Sequential";
        writer.Append($"[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.{kind}");
        writer.AppendIf(structLayout.Pack > 0, $", Pack = {structLayout.Pack}");
        if (StructLayoutReader.TryGetSize(s, out var size))
        {
            writer.Append($", Size = {size}");
        }

        writer.AppendLine(")]");
        writer.AppendLine($"public unsafe struct {HostMirrorName}");
        using (writer.Scope())
        {
            foreach (var member in StructAbiMembers.Get(s))
            {
                var name = member.Name.ToCSharpIdentifier();
                if (member is { Kind: StructAbiMemberKind.FixedBuffer, Symbol: IFieldSymbol })
                {
                    // C# allows a fixed buffer only of a primitive type, so the element always has a keyword
                    var element = member.Type.SpecialType.GetCsharpKeyword() ?? "byte";
                    writer.AppendLine($"public fixed {element} {name}[{member.ElementCount}];");
                    continue;
                }

                writer.Append("public ");
                writer.AppendFullyQualifiedCsharpName(member.Type);
                writer.AppendLine($" {name};");
            }

            WriteFieldOffsetCheck(writer, s);
        }
    }

    /// <summary>The static field of the host mirror that tells whether its fields sit at the offsets of the struct's.</summary>
    internal const string HostMirrorOffsetsMatch = "FieldOffsetsMatch";

    // [WHY] The size check alone misses two fields that trade places. Each field that generated code can name on the
    // struct itself (an ordinary field that is not private) is compared with the mirror's, by Unsafe.ByteOffset from
    // the start of a default value; the hidden fields and the backing fields of auto-properties have no name to compare.
    // The result is computed once, when the mirror is first used.
    private static void WriteFieldOffsetCheck(SourceWriter writer, INamedTypeSymbol s)
    {
        const string Unsafe = "System.Runtime.CompilerServices.Unsafe";
        writer.AppendLine($"public static readonly bool {HostMirrorOffsetsMatch} = CheckFieldOffsets();");
        writer.AppendLine("private static bool CheckFieldOffsets()");
        using (writer.Scope())
        {
            if (!HasNamedField(s))
            {
                writer.AppendLine("return true;");
                return;
            }

            writer.AppendFullyQualifiedCsharpName(s);
            writer.AppendLine(" s = default;");
            writer.Append($"ref var m = ref {Unsafe}.As<");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.AppendLine($", {HostMirrorName}>(ref s);");
            writer.Append("return true");
            foreach (var member in StructAbiMembers.Get(s))
            {
                if (!IsNamedField(member))
                {
                    continue;
                }

                var name = member.Name.ToCSharpIdentifier();
                writer.AppendLine(" &&");
                writer.Append($"{Unsafe}.ByteOffset(ref {Unsafe}.As<");
                writer.AppendFullyQualifiedCsharpName(s);
                writer.Append($", byte>(ref s), ref {Unsafe}.As<");
                writer.AppendFullyQualifiedCsharpName(member.Type);
                writer.Append($", byte>(ref {Unsafe}.AsRef(in s.{name}))) == {Unsafe}.ByteOffset(ref {Unsafe}.As<{HostMirrorName}, byte>(ref m), ref {Unsafe}.As<");
                writer.AppendFullyQualifiedCsharpName(member.Type);
                writer.Append($", byte>(ref m.{name}))");
            }

            writer.AppendLine(";");
        }
    }

    private static bool HasNamedField(INamedTypeSymbol s)
    {
        foreach (var member in StructAbiMembers.Get(s))
        {
            if (IsNamedField(member))
            {
                return true;
            }
        }

        return false;
    }

    // An ordinary field that generated code can name on the struct (not private)
    private static bool IsNamedField(StructAbiMember member) =>
        member is { Kind: StructAbiMemberKind.Field, Symbol: IFieldSymbol field } &&
        field.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Protected
            or Accessibility.ProtectedAndInternal);

    /// <summary>
    /// Whether <paramref name="member"/> is a reference type other than an array-like one, or a compiler-generated field
    /// that G# does not know, which the shadow struct and its marshaling leave out (GSH0078).
    /// </summary>
    internal static bool IsUnrepresentableReference(StructAbiMember member) =>
        member.Kind == StructAbiMemberKind.UnknownHiddenField ||
        (member.Type.IsReferenceType && !member.Type.IsArrayLikeType());

    private static void MapToNativeField(SourceWriter writer, StructAbiMember field, int fieldSize)
    {
        if (field.Kind is StructAbiMemberKind.FixedBuffer or StructAbiMemberKind.InlineArrayElement)
        {
            // [WHY] An element block is copied as bytes, so the shadow holds all ElementCount elements and never names the (possibly private) element field's type.
            writer.AppendLine($"public fixed byte {field.Name.ToCSharpIdentifier()}[{fieldSize}];");
        }
        else if (field.Type.IsArrayLikeType())
        {
            writer.AppendLine($"public global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {field.Name.ToCSharpIdentifier()};");
        }
        else if (TypeLayoutCalculator.IsUserStruct(field.Type))
        {
            writer.Append("public ");
            writer.AppendAotStructPath(field.Type);
            writer.AppendLine($" {field.Name.ToCSharpIdentifier()};");
        }
        else if (field.Type.SpecialType != SpecialType.None)
        {
            writer.Append("public ");
            writer.AppendFullyQualifiedCsharpName(field.Type);
            writer.AppendLine($" {field.Name.ToCSharpIdentifier()};");
        }
        else if (field.Type.TypeKind == TypeKind.Enum)
        {
            writer.Append("public ");
            writer.AppendFullyQualifiedCsharpName(field.Type);
            writer.AppendLine($" {field.Name.ToCSharpIdentifier()};");
        }
        else
        {
            // Core vector types (Float4, etc.) or explicit structs are emitted as fixed byte arrays.
            // This is a workaround to bypass CLR nesting bugs (TypeLoadException) when explicit structs are nested inside other structs.
            writer.AppendLine($"public fixed byte {field.Name.ToCSharpIdentifier()}[{fieldSize}];");
        }
    }
}
