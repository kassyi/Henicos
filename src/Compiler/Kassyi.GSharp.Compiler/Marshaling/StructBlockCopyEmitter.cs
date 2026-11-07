
namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits host mirror struct bindings, block memory copies, and inline array copying for marshaled structs.</summary>
internal static class StructBlockCopyEmitter
{
    // [WHY] The members are then read and written by the names of the mirror, whose fields name the hidden fields of
    // the captured primary constructor parameters (NativeStructEmitter.NeedsHostMirror). The size and offset checks
    // catch a mirror that does not have the CLR layout of S.
    internal static void WriteHostMirrorBinding(SourceWriter writer, INamedTypeSymbol s, bool hasMirror)
    {
        if (!hasMirror)
        {
            return;
        }

        writer.Append($"if ({HostTypeNames.Unsafe}.SizeOf<");
        writer.AppendFullyQualifiedCsharpName(s);
        writer.Append($">() != {HostTypeNames.Unsafe}.SizeOf<");
        writer.AppendAotStructPath(s);
        writer.Append($".{NativeStructEmitter.HostMirrorName}>()) ");
        writer.Append("global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowSizeMismatch(\"");
        writer.Append(TypeLayoutCalculator.FormatTypeName(s));
        writer.Append($"\", {HostTypeNames.Unsafe}.SizeOf<");
        writer.AppendAotStructPath(s);
        writer.Append($".{NativeStructEmitter.HostMirrorName}>(), {HostTypeNames.Unsafe}.SizeOf<");
        writer.AppendFullyQualifiedCsharpName(s);
        writer.AppendLine(">());");
        writer.Append("if (!");
        writer.AppendAotStructPath(s);
        writer.Append($".{NativeStructEmitter.HostMirrorName}.{NativeStructEmitter.HostMirrorOffsetsMatch}) ");
        writer.Append("global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch(\"");
        writer.Append(TypeLayoutCalculator.FormatTypeName(s));
        writer.AppendLine("\");");
        writer.Append($"ref var host = ref {HostTypeNames.Unsafe}.As<");
        writer.AppendFullyQualifiedCsharpName(s);
        writer.Append(", ");
        writer.AppendAotStructPath(s);
        writer.AppendLine($".{NativeStructEmitter.HostMirrorName}>(ref {StructMarshalingEmitter.MirroredHostParameter});");
    }

    // [WHY] An [InlineArray(N)] struct and a fixed buffer are copied as one block of ElementCount elements: the element
    // field of an inline array is usually private, and a fixed buffer cannot be assigned. The inline array's host value
    // is the whole struct, so it is copied by its own address (Unsafe.Copy infers its type from host). An inline array
    // whose element lays out differently on the device, or holds arrays, is copied element by element instead
    // (host[k] is the element, whatever the field's accessibility).
    internal static bool WriteBlockCopy(SourceWriter writer, StructAbiMember member, bool toNative,
        ITranspilerDialect dialect)
    {
        var name = member.Name.ToCSharpIdentifier();
        switch (member.Kind)
        {
            case StructAbiMemberKind.InlineArrayElement:
                writer.AppendLine($"fixed (byte* pField = native.{name})");
                using (writer.Scope())
                {
                    if (NeedsElementCopy(member.Type, dialect))
                    {
                        WriteInlineArrayElementCopy(writer, member, toNative, dialect);
                    }
                    else
                    {
                        writer.AppendLine(toNative
                            ? $"{HostTypeNames.Unsafe}.Copy(pField, ref host);"
                            : $"{HostTypeNames.Unsafe}.Copy(ref host, pField);");
                    }
                }

                return true;
            case StructAbiMemberKind.FixedBuffer:
                // C# allows a fixed buffer only of a primitive type, so the element always has a keyword
                var element = member.Type.SpecialType.GetCsharpKeyword() ?? "byte";
                var byteCount = $"{member.ElementCount} * sizeof({element})";
                writer.AppendLine($"fixed (byte* pField = native.{name})");
                using (writer.Scope())
                {
                    writer.AppendLine($"fixed ({element}* pHost = host.{name})");
                    using (writer.Scope())
                    {
                        writer.AppendLine($"global::System.Buffer.MemoryCopy({(toNative ? "pHost, pField" : "pField, pHost")}, {byteCount}, {byteCount});");
                    }
                }

                return true;
            default:
                return false;
        }
    }

    // Whether an inline array element is copied one by one: a user struct that is not Identical, or an array-like value
    // (a GSharpArray on the device, a reference on the host)
    internal static bool NeedsElementCopy(ITypeSymbol elementType, ITranspilerDialect dialect) =>
        elementType.IsArrayLikeType() ||
        elementType is INamedTypeSymbol named &&
        TypeLayoutCalculator.IsUserStruct(named) &&
        TypeLayoutCalculator.GetStructLayout(named, dialect).Transfer != TransferClass.Identical;

    // for (int k = 0; k < <N>; k++) { <copy host[k] and ((<element shadow>*)pField)[k]> }
    internal static void WriteInlineArrayElementCopy(SourceWriter writer, StructAbiMember member, bool toNative,
        ITranspilerDialect dialect)
    {
        writer.AppendLine($"for (int k = 0; k < {member.ElementCount}; k++)");
        using (writer.Scope())
        {
            if (member.Type.TryGetArrayLikeElementType(out var arrayElementType))
            {
                const string NativeElement = "((global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy*)pField)[k]";
                if (toNative)
                {
                    StructMarshalMethodEmitter.WriteMarshalArrayMember(writer, "host[k]", NativeElement, arrayElementType, dialect);
                }
                else
                {
                    StructUnmarshalMethodEmitter.WriteUnmarshalArrayMember(writer, "host[k]", NativeElement, arrayElementType, dialect);
                }

                return;
            }

            var shadow = AotHostNames.GetAotStructPath(member.Type);
            if (toNative)
            {
                writer.AppendMarshalMethodName(member.Type);
                writer.AppendLine($"(ref host[k], ref (({shadow}*)pField)[k], {StructArrayHelperEmitter.ArraysParameter(dialect)}, hStream);");
            }
            else
            {
                writer.AppendUnmarshalMethodName(member.Type);
                writer.AppendLine($"(ref host[k], ref (({shadow}*)pField)[k], hStream);");
            }
        }
    }


    /// <summary>
    /// Emits <c>var handle = GCHandle.Alloc(&lt;pinned&gt;, Pinned); try { &lt;copy&gt; } finally { handle.Free(); }</c>,
    /// where the copy moves <c>size</c> bytes between the pinned host array and <paramref name="device"/>.
    /// </summary>
    /// <param name="writer">The writer of the generated <c>Marshal_</c> / <c>Unmarshal_</c> method.</param>
    /// <param name="pinned">The host array, or the shadow array of its converted elements.</param>
    /// <param name="device">The device pointer expression (<c>native.Data</c>).</param>
    /// <param name="toDevice"><c>cuMemcpyHtoDAsync_v2</c> when true, <c>cuMemcpyDtoHAsync_v2</c> otherwise.</param>
    internal static void WritePinnedCopy(SourceWriter writer, string pinned, string device, bool toDevice)
    {
        writer.AppendLine(
            $"var handle = {HostTypeNames.GCHandle}.Alloc({pinned}, " +
            $"{HostTypeNames.GCHandleType}.Pinned);");
        writer.AppendLine("try");
        using (writer.Scope())
        {
            writer.AppendLine(toDevice
                ? $"{HostTypeNames.CudaException}.ThrowIfError({HostTypeNames.CudaDriverApi}.cuMemcpyHtoDAsync_v2(" +
                $"{device}, handle.AddrOfPinnedObject(), (global::System.IntPtr)size, hStream));"
                : $"{HostTypeNames.CudaException}.ThrowIfError({HostTypeNames.CudaDriverApi}.cuMemcpyDtoHAsync_v2(" +
                $"handle.AddrOfPinnedObject(), {device}, (global::System.IntPtr)size, hStream));");
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            writer.AppendLine("handle.Free();");
        }
    }


    internal static void WriteArrayElementSize(SourceWriter writer, string host, ITypeSymbol elementType)
    {
        writer.Append($"int size = checked({host}.Length * sizeof(");
        if (TypeLayoutCalculator.IsUserStruct(elementType))
        {
            writer.AppendAotStructPath(elementType);
        }
        else
        {
            writer.AppendFullyQualifiedCsharpName(elementType);
        }

        writer.AppendLine("));");
    }
}
