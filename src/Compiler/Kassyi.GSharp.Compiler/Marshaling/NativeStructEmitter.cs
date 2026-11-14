namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits explicit layout shadow structs representing C# structs in native device memory.</summary>
internal static class NativeStructEmitter
{
    /// <summary>Emits the explicit struct declaration with computed field offsets for <paramref name="dialect"/>.</summary>
    internal static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s,
        ITranspilerDialect dialect, IAssemblySymbol launcherAssembly) =>
        WriteNativeStructDeclaration(writer, s, TypeLayoutCalculator.GetStructLayout(s, dialect), dialect,
            launcherAssembly);

    /// <summary>Emits the explicit struct declaration with computed field offsets from <paramref name="structLayout"/>.</summary>
    internal static void WriteNativeStructDeclaration(SourceWriter writer, INamedTypeSymbol s,
        StructLayoutInfo structLayout, ITranspilerDialect dialect, IAssemblySymbol launcherAssembly)
    {
        var parents = AotHostNames.GetEnclosingCustomStructs(s);

        foreach (var p in parents)
        {
            writer.AppendLine($"public unsafe partial struct {AotHostNames.GetNativeStructName(p)}");
            writer.AppendLine("{");
        }

        writer.AppendLine(
            $"[{HostTypeNames.StructLayout}({HostTypeNames.LayoutKind}.Explicit, Size = {structLayout.Device.Size})]");
        writer.Append($"public unsafe partial struct {AotHostNames.GetNativeStructName(s)}");
        writer.AppendLine();

        using (writer.Scope())
        {
            foreach (var (field, offset, fieldLayout) in structLayout.DeviceFields)
            {
                // [WHY] A reference type other than an array has no device layout: GSH0078 reports it, and the shadow
                // leaves it out so that the generated code still compiles.
                if (IsOmittedFromShadow(field))
                {
                    continue;
                }

                writer.Append($"public const string DebugName_{field.Name} = \"");
                writer.AppendFullyQualifiedCsharpName(field.Type);
                writer.AppendLine("\";");
                writer.AppendLine($"[{HostTypeNames.FieldOffset}({offset})]");

                MapToNativeField(writer, field, fieldLayout.Size, dialect);
            }

            if (NeedsHostMirror(s, structLayout, launcherAssembly))
            {
                WriteHostMirror(writer, s, structLayout, launcherAssembly);
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
    /// which generated C# cannot name (#495), the backing field of a property whose accessors use the <c>field</c>
    /// keyword, which generated C# must not reach through the accessors: the transfer copies the stored value, or a
    /// field or fixed buffer that code generated into <paramref name="launcherAssembly"/> cannot name (a private field,
    /// an internal field of another assembly; #808).
    /// </summary>
    /// <remarks>
    /// The mirror declares the fields of <paramref name="s"/> in <see cref="StructAbiMembers"/> order, which is the CLR
    /// order, with the same types, layout kind and <c>Pack</c>, so it has the CLR layout of <paramref name="s"/>; the hidden
    /// fields are named after their parameters. The marshaling reinterprets the host value as the mirror
    /// (<c>Unsafe.As</c>) after checking that both have the same size.
    /// </remarks>
    internal static bool NeedsHostMirror(INamedTypeSymbol s, StructLayoutInfo structLayout,
        IAssemblySymbol launcherAssembly)
    {
        if (structLayout.Transfer == TransferClass.Identical)
        {
            return false;
        }

        foreach (var member in StructAbiMembers.Get(s))
        {
            if (member.Kind is StructAbiMemberKind.CapturedParameter or StructAbiMemberKind.FieldBackedProperty ||
                IsUnnameableField(member, launcherAssembly))
            {
                return true;
            }
        }

        return false;
    }

    // A field or fixed buffer that the marshaling would read as host.<name> but that generated code cannot name
    private static bool IsUnnameableField(StructAbiMember member, IAssemblySymbol launcherAssembly) =>
        member is { Kind: StructAbiMemberKind.Field or StructAbiMemberKind.FixedBuffer, Symbol: IFieldSymbol field } &&
        !HostFieldAccess.CanName(field, launcherAssembly);

    // [StructLayout(LayoutKind.<Sequential | Explicit | Auto>[, Pack = <n>][, Size = <n>])]
    // public unsafe struct __gsharp_host_mirror {
    // [[FieldOffset(<n>)]] public <type> <name>; ... [[FieldOffset(<n>)]] public fixed <element> <name>[<n>];
    // public static readonly bool FieldOffsetsMatch = CheckFieldOffsets();
    // private static bool CheckFieldOffsets() { <S> s = default; ref <mirror> m = ref Unsafe.As<S, <mirror>>(ref s);
    // return <offset of m.F> == <host offset of F> && <offset of s.G> == <offset of m.G> && ...; } }
    private static void WriteHostMirror(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout,
        IAssemblySymbol launcherAssembly)
    {
        var plan = HostMirrorPlan.Create(s, structLayout, launcherAssembly);
        writer.Append($"[{HostTypeNames.StructLayout}({HostTypeNames.LayoutKind}.{plan.LayoutKind}");
        writer.AppendIf(plan.Pack > 0, $", Pack = {plan.Pack}");
        writer.AppendIf(plan.Size > 0, $", Size = {plan.Size}");
        writer.AppendLine(")]");
        writer.AppendLine($"public unsafe struct {HostMirrorName}");
        using (writer.Scope())
        {
            foreach (var field in plan.Fields)
            {
                WriteHostMirrorField(writer, field);
            }

            WriteFieldOffsetCheck(writer, s, plan);
        }
    }

    // [FieldOffset(<n>)] (explicit layout only) public <type> <name>; or public fixed <element> <name>[<n>];
    private static void WriteHostMirrorField(SourceWriter writer, HostMirrorField field)
    {
        var member = field.Member;
        var name = member.Name.ToCSharpIdentifier();
        if (field.FieldOffset is { } fieldOffset)
        {
            writer.AppendLine($"[{HostTypeNames.FieldOffset}({fieldOffset})]");
        }

        if (member is { Kind: StructAbiMemberKind.FixedBuffer, Symbol: IFieldSymbol })
        {
            // C# allows a fixed buffer only of a primitive type, so the element always has a keyword
            var element = member.Type.SpecialType.GetCsharpKeyword() ?? "byte";
            writer.AppendLine($"public fixed {element} {name}[{member.ElementCount}];");
            return;
        }

        writer.Append("public ");
        writer.AppendFullyQualifiedCsharpName(member.Type);
        writer.AppendLine($" {name};");
    }

    /// <summary>The static field of the host mirror that tells whether its fields sit at the offsets of the struct's.</summary>
    internal const string HostMirrorOffsetsMatch = "FieldOffsetsMatch";

    // [WHY] The size check alone misses two fields that trade places, and when every field is private there is no
    // field of the struct to compare with. Each mirror field is compared, by Unsafe.ByteOffset from the start of a
    // default value, with the same field of the struct when the launcher can name it, otherwise with the host offset
    // the compiler predicts (HostMirrorPlan), when the CLR does not decide the layout at run time. A fixed buffer and a
    // pointer field have no ref that Unsafe.As takes and are left to the size check. The result is computed once, when
    // the mirror is first used.
    private static void WriteFieldOffsetCheck(SourceWriter writer, INamedTypeSymbol s, HostMirrorPlan plan)
    {
        const string Unsafe = HostTypeNames.Unsafe;
        writer.AppendLine($"public static readonly bool {HostMirrorOffsetsMatch} = CheckFieldOffsets();");
        writer.AppendLine("private static bool CheckFieldOffsets()");
        using (writer.Scope())
        {
            if (!plan.HasOffsetCheck)
            {
                writer.AppendLine("return true;");
                return;
            }

            writer.AppendFullyQualifiedCsharpName(s);
            writer.AppendLine(" s = default;");
            writer.Append($"ref {HostMirrorName} m = ref {Unsafe}.As<");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.AppendLine($", {HostMirrorName}>(ref s);");
            writer.Append("return true");
            foreach (var field in plan.Fields)
            {
                if (field.Check == HostMirrorOffsetCheck.None)
                {
                    continue;
                }

                var name = field.Member.Name.ToCSharpIdentifier();
                writer.AppendLine(" &&");
                if (field.Check == HostMirrorOffsetCheck.PredictedOffset)
                {
                    WriteByteOffset(writer, $"{HostMirrorName}, byte>(ref m)", field.Member.Type, $"ref m.{name}");
                    writer.Append($" == {field.ExpectedOffset}");
                    continue;
                }

                writer.Append($"{Unsafe}.ByteOffset(ref {Unsafe}.As<");
                writer.AppendFullyQualifiedCsharpName(s);
                writer.Append($", byte>(ref s), ref {Unsafe}.As<");
                writer.AppendFullyQualifiedCsharpName(field.Member.Type);
                writer.Append($", byte>(ref {Unsafe}.AsRef(in s.{name}))) == ");
                WriteByteOffset(writer, $"{HostMirrorName}, byte>(ref m)", field.Member.Type, $"ref m.{name}");
            }

            writer.AppendLine(";");
        }
    }

    // Unsafe.ByteOffset(ref Unsafe.As<<origin>, ref Unsafe.As<<type>, byte>(<field ref>))
    private static void WriteByteOffset(SourceWriter writer, string origin, ITypeSymbol type, string fieldRef)
    {
        const string Unsafe = HostTypeNames.Unsafe;
        writer.Append($"{Unsafe}.ByteOffset(ref {Unsafe}.As<{origin}, ref {Unsafe}.As<");
        writer.AppendFullyQualifiedCsharpName(type);
        writer.Append($", byte>({fieldRef}))");
    }

    /// <summary>
    /// Whether <paramref name="member"/> is a reference type other than an array-like one, or a compiler-generated field
    /// that G# does not know, which the shadow struct and its marshaling leave out (GSH0078).
    /// </summary>
    internal static bool IsOmittedFromShadow(StructAbiMember member) =>
        member.Kind == StructAbiMemberKind.UnknownHiddenField ||
        (member.Type.IsReferenceType && !member.Type.IsArrayLikeType());

    private static void MapToNativeField(SourceWriter writer, StructAbiMember field, int fieldSize,
        ITranspilerDialect dialect)
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
        else if (HostShadowStructs.UsesShadow(field.Type, dialect))
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
