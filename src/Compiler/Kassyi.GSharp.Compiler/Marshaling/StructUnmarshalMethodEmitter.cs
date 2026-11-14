
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


    private static void WriteUnmarshalMember(SourceWriter writer, StructAbiMember member, ITranspilerDialect dialect)
    {
        if (NativeStructEmitter.IsOmittedFromShadow(member) ||
            StructBlockCopyEmitter.TryWriteBlockCopy(writer, member, toNative: false, dialect))
        {
            return;
        }

        if (member.Type.IsArrayLikeType())
        {
            var name = member.Name.ToCSharpIdentifier();
            WriteUnmarshalArrayMember(writer, $"host.{name}", $"native.{name}", member.Type, dialect);
        }
        else if (HostShadowStructs.UsesShadow(member.Type, dialect))
        {
            writer.AppendUnmarshalMethodName(member.Type);
            using (writer.Parentheses())
            {
                writer.Append($"ref host.{member.Name.ToCSharpIdentifier()}, ref native.{member.Name.ToCSharpIdentifier()}, hStream");
            }

            writer.AppendLine(";");
        }
        else
        {
            if (member.Type.IsReferenceType || member.Type.SpecialType != SpecialType.None)
            {
                writer.AppendLine($"host.{member.Name.ToCSharpIdentifier()} = native.{member.Name.ToCSharpIdentifier()};");
            }
            else
            {
                writer.AppendLine($"fixed (byte* pField = native.{member.Name.ToCSharpIdentifier()})");
                using (writer.Scope())
                {
                    writer.Append($"host.{member.Name.ToCSharpIdentifier()} = *((");
                    writer.AppendFullyQualifiedCsharpName(member.Type);
                    writer.AppendLine("*)pField);");
                }
            }
        }
    }

    // An array field, or the view of another array-like field (Memory<T>, ArraySegment<T>, ..., #1267)
    internal static void WriteUnmarshalArrayMember(SourceWriter writer, string host, string native,
        ITypeSymbol arrayLikeType, ITranspilerDialect dialect)
    {
        if (arrayLikeType is not IArrayTypeSymbol { ElementType: var elementType })
        {
            ArrayViewFieldEmitter.WriteUnmarshal(writer, ArrayFieldView.Create(arrayLikeType, dialect), host, native,
                dialect);
            return;
        }

        if (!dialect.IsCuda)
        {
            WriteCpuUnmarshalArrayMember(writer, host, native, elementType, dialect);
            return;
        }

        writer.AppendLine(
            $"if ({host} != null && {native}.Data != global::System.IntPtr.Zero)");
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

    // if (<host> != null) {
    //   List<IntPtr> pinnedHandles = allocatedPtrs ?? throw new ArgumentNullException("allocatedPtrs");
    //   [<element shadow>[] shadow = new <element shadow>[<host>.Length];
    //    for (...) Marshal_<element>(ref <host>[i], ref shadow[i], ...);]
    //   GCHandle handle = GCHandle.Alloc(<host or shadow>, GCHandleType.Pinned);
    //   pinnedHandles.Add(GCHandle.ToIntPtr(handle));
    //   <native>.Data = handle.AddrOfPinnedObject(); <native>.Length = <host>.Length; }

    private static void WriteCpuUnmarshalArrayMember(SourceWriter writer, string host, string native,
        ITypeSymbol elementType, ITranspilerDialect dialect)
    {
        if (!KernelParameterShape.IsConverted(elementType, dialect))
        {
            return;
        }

        writer.AppendLine($"if ({host} != null && {native}.Data != global::System.IntPtr.Zero)");
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
