
namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits Unmarshal_ methods that convert native device representations back into C# structs.</summary>
internal static class StructUnmarshalMethodEmitter
{
    internal static void WriteUnmarshalMethod(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout,
        ITranspilerDialect dialect, IAssemblySymbol launcherAssembly)
    {
        var hasMirror = NativeStructEmitter.NeedsHostMirror(s, structLayout, launcherAssembly);
        writer.Append("public static unsafe void ");
        writer.AppendUnmarshalMethodName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(hasMirror ? $" {StructMarshalingEmitter.MirroredHostParameter}, ref " : " host, ref ");
            writer.AppendAotStructPath(s);
            writer.Append(" native, global::System.IntPtr hStream = default");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            if (structLayout.Transfer == TransferClass.Identical)
            {
                writer.Append($"host = {HostTypeNames.Unsafe}.As<");
                writer.AppendAotStructPath(s);
                writer.Append(", ");
                writer.AppendFullyQualifiedCsharpName(s);
                writer.AppendLine(">(ref native);");
            }
            else
            {
                StructBlockCopyEmitter.WriteHostMirrorBinding(writer, s, hasMirror);
                foreach (var field in structLayout.DeviceFields)
                {
                    if (field.IsCopied)
                    {
                        WriteUnmarshalMember(writer, field.Member, dialect);
                    }
                }
            }
        }

        writer.AppendLine();
    }

    /// <summary>
    /// Emits <c>UnmarshalArrays_{key}</c>, which copies device writes back into the elements of arrays and writable
    /// array views held by an <c>in</c> struct parameter without modifying any field of the struct itself.
    /// </summary>
    internal static void WriteUnmarshalArraysMethod(SourceWriter writer, INamedTypeSymbol s,
        StructLayoutInfo structLayout, ITranspilerDialect dialect, IAssemblySymbol launcherAssembly)
    {
        if (!HostShadowStructs.HoldsArrayFields(s))
        {
            return;
        }

        var hasMirror = NativeStructEmitter.NeedsHostMirror(s, structLayout, launcherAssembly);
        writer.Append("public static unsafe void ");
        writer.AppendUnmarshalArraysMethodName(s);
        using (writer.Parentheses())
        {
            writer.Append("in ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(hasMirror ? $" {StructMarshalingEmitter.MirroredHostParameter}, ref " : " host, ref ");
            writer.AppendAotStructPath(s);
            writer.Append(" native, global::System.IntPtr hStream = default");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            StructBlockCopyEmitter.WriteHostMirrorBinding(writer, s, hasMirror, readOnly: true);
            foreach (var field in structLayout.DeviceFields)
            {
                if (field.IsCopied)
                {
                    WriteUnmarshalArraysMember(writer, field.Member, dialect);
                }
            }
        }

        writer.AppendLine();
    }

    private static void WriteUnmarshalArraysMember(SourceWriter writer, StructAbiMember member,
        ITranspilerDialect dialect)
    {
        if (NativeStructEmitter.IsOmittedFromShadow(member))
        {
            return;
        }

        var kind = NativeStructEmitter.ClassifyField(member, dialect);
        switch (kind)
        {
            case NativeFieldKind.ArrayProxy:
                var proxyName = member.Name.ToCSharpIdentifier();
                WriteUnmarshalArrayLikeMember(writer, $"host.{proxyName}", $"native.{proxyName}", member.Type, dialect,
                    assignNullArray: false);
                break;
            case NativeFieldKind.UserStruct when HostShadowStructs.HoldsArrayFields(member.Type) ||
                                                 member.Type.IsNullableArrayLike():
                var structName = member.Name.ToCSharpIdentifier();
                writer.AppendUnmarshalArraysMethodName(member.Type);
                using (writer.Parentheses())
                {
                    writer.Append($"in host.{structName}, ref native.{structName}, hStream");
                }

                writer.AppendLine(";");
                break;
            case NativeFieldKind.FixedBuffer when member.Kind == StructAbiMemberKind.InlineArrayElement &&
                                                 (member.Type.IsArrayLikeType() ||
                                                  HostShadowStructs.HoldsArrayFields(member.Type)):
                WriteInlineArrayUnmarshalArrays(writer, member, dialect);
                break;
        }
    }

    private static void WriteInlineArrayUnmarshalArrays(SourceWriter writer, StructAbiMember member,
        ITranspilerDialect dialect)
    {
        var name = member.Name.ToCSharpIdentifier();
        writer.AppendLine($"fixed (byte* pField = native.{name})");
        using (writer.Scope())
        {
            writer.AppendLine($"for (int k = 0; k < {member.ElementCount}; k++)");
            using (writer.Scope())
            {
                if (member.Type.IsArrayLikeType())
                {
                    WriteUnmarshalArrayLikeMember(writer, "host[k]",
                        "((global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy*)pField)[k]", member.Type, dialect,
                        assignNullArray: false);
                    return;
                }

                var shadow = AotHostNames.GetAotStructPath(member.Type);
                writer.AppendUnmarshalArraysMethodName(member.Type);
                writer.AppendLine($"(in host[k], ref (({shadow}*)pField)[k], hStream);");
            }
        }
    }


    private static void WriteUnmarshalMember(SourceWriter writer, StructAbiMember member, ITranspilerDialect dialect)
    {
        if (NativeStructEmitter.IsOmittedFromShadow(member))
        {
            return;
        }

        var kind = NativeStructEmitter.ClassifyField(member, dialect);
        switch (kind)
        {
            case NativeFieldKind.FixedBuffer:
                StructBlockCopyEmitter.WriteBlockCopy(writer, member, toNative: false, dialect);
                break;
            case NativeFieldKind.ArrayProxy:
                WriteUnmarshalArrayProxy(writer, member, dialect);
                break;
            case NativeFieldKind.UserStruct:
                WriteUnmarshalUserStruct(writer, member);
                break;
            case NativeFieldKind.DirectValue:
                StructBlockCopyEmitter.WriteDirectCopy(writer, member, toNative: false);
                break;
            case NativeFieldKind.FixedBytesFallback:
                StructBlockCopyEmitter.WriteFixedBytesCopy(writer, member, toNative: false);
                break;
            default:
                throw new InvalidOperationException(
                    $"The native field kind {kind} of '{member.Name}' has no unmarshal code.");
        }
    }

    private static void WriteUnmarshalArrayProxy(SourceWriter writer, StructAbiMember member,
        ITranspilerDialect dialect)
    {
        var name = member.Name.ToCSharpIdentifier();
        WriteUnmarshalArrayLikeMember(writer, $"host.{name}", $"native.{name}", member.Type, dialect);
    }

    private static void WriteUnmarshalUserStruct(SourceWriter writer, StructAbiMember member)
    {
        var name = member.Name.ToCSharpIdentifier();
        writer.AppendUnmarshalMethodName(member.Type);
        using (writer.Parentheses())
        {
            writer.Append($"ref host.{name}, ref native.{name}, hStream");
        }

        writer.AppendLine(";");
    }

    // An array field, or the view of another array-like field (Memory<T>, ArraySegment<T>, ..., #1267)
    internal static void WriteUnmarshalArrayLikeMember(SourceWriter writer, string host, string native,
        ITypeSymbol arrayLikeType, ITranspilerDialect dialect, bool assignNullArray = true)
    {
        if (arrayLikeType is not IArrayTypeSymbol { ElementType: var elementType })
        {
            ArrayViewFieldEmitter.WriteUnmarshal(writer, ArrayFieldView.Create(arrayLikeType, dialect), host, native,
                dialect);
            return;
        }

        if (!dialect.IsCuda)
        {
            WriteCpuUnmarshalArrayMember(writer, host, native, elementType, dialect, assignNullArray);
            return;
        }

        if (assignNullArray)
        {
            WriteNullArrayAssigned(writer, host, native);
            writer.AppendLine($"else if ({host} != null)");
        }
        else
        {
            writer.AppendLine($"if ({native}.Data != global::System.IntPtr.Zero && {host} != null)");
        }

        using (writer.Scope())
        {
            StructBlockCopyEmitter.WriteArrayElementSize(writer, host, elementType);

            writer.AppendLine("if (size > 0)");
            using (writer.Scope())
            {
                if (HostShadowStructs.UsesShadow(elementType, HostDialects.Cuda))
                {
                    StructMarshalMethodEmitter.WriteShadowArray(writer, "tempArray", host, elementType);

                    StructBlockCopyEmitter.WritePinnedCopy(writer, "tempArray", $"{native}.Data", toDevice: false);

                    writer.AppendLine($"for (int i = 0; i < {host}.Length; i++)");
                    using (writer.Scope())
                    {
                        writer.AppendUnmarshalMethodName(elementType);
                        writer.AppendLine($"(ref {host}[i], ref tempArray[i], hStream);");
                    }
                }
                else
                {
                    StructBlockCopyEmitter.WritePinnedCopy(writer, host, $"{native}.Data", toDevice: false);
                }
            }
        }
    }

    // if (<native>.Data == IntPtr.Zero) <host> = default;
    // [WHY] The kernel assigned a null array to the field (a null array has no address). The marshaled arrays are
    // pinned or copied in place, so the host array keeps its elements, but the field itself must become what the
    // kernel wrote, as C# does (#1841). An array the launcher marshaled is never at address 0: an empty array gets
    // EmptyArrayData and a null host array stays null.
    private static void WriteNullArrayAssigned(SourceWriter writer, string host, string native)
    {
        writer.AppendLine($"if ({native}.Data == global::System.IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.AppendLine($"{host} = default;");
        }
    }

    // if (<host> != null) {
    //   List<IntPtr> pinnedHandles = allocatedPtrs ?? throw new ArgumentNullException("allocatedPtrs");
    //   [<element shadow>[] shadow = new <element shadow>[<host>.Length];
    //    for (...) Marshal_<element>(ref <host>[i], ref shadow[i], ...);]
    //   GCHandle handle = GCHandle.Alloc(<host or shadow>, GCHandleType.Pinned);
    //   pinnedHandles.Add(GCHandle.ToIntPtr(handle));
    //   <native>.Data = handle.AddrOfPinnedObject(); <native>.Length = <host>.Length; }

    private static void WriteCpuUnmarshalArrayMember(SourceWriter writer, string host, string native,
        ITypeSymbol elementType, ITranspilerDialect dialect, bool assignNullArray = true)
    {
        if (assignNullArray)
        {
            WriteNullArrayAssigned(writer, host, native);
        }

        if (!KernelParameterShape.IsConverted(elementType, dialect))
        {
            return;
        }

        writer.AppendLine(assignNullArray
            ? $"else if ({host} != null)"
            : $"if ({native}.Data != global::System.IntPtr.Zero && {host} != null)");
        using (writer.Scope())
        {
            writer.AppendAotStructPath(elementType);
            writer.Append("* shadow = (");
            writer.AppendAotStructPath(elementType);
            writer.AppendLine($"*){native}.Data;");
            writer.AppendLine($"for (int i = 0; i < {host}.Length; i++)");
            using (writer.Scope())
            {
                writer.AppendUnmarshalMethodName(elementType);
                writer.AppendLine($"(ref {host}[i], ref shadow[i], hStream);");
            }
        }
    }

}
