
namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits Marshal_ methods that convert C# structs into native device representations.</summary>
internal static class StructMarshalMethodEmitter
{
    internal static void WriteMarshalMethod(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout,
        ITranspilerDialect dialect)
    {
        var hasMirror = NativeStructEmitter.NeedsHostMirror(s, structLayout);
        writer.Append("public static unsafe void ");
        writer.AppendMarshalMethodName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(hasMirror ? $" {StructMarshalingEmitter.MirroredHostParameter}, ref " : " host, ref ");
            writer.AppendAotStructPath(s);
            // CUDA records one device region per host array (DeviceArrayRegions, #707); the CPU records GCHandles
            writer.Append(dialect.IsCuda
                ? $" native, bool isDevicePtr, {StructMarshalingEmitter.DeviceArrayRegionsType}? {StructMarshalingEmitter.CudaArraysParameter}, global::System.IntPtr hStream = default"
                : $" native, bool isDevicePtr, global::System.Collections.Generic.List<global::System.IntPtr>? {StructMarshalingEmitter.CpuArraysParameter}, global::System.IntPtr hStream = default");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            if (structLayout.Transfer == TransferClass.Identical)
            {
                writer.Append($"native = {HostTypeNames.Unsafe}.As<");
                writer.AppendFullyQualifiedCsharpName(s);
                writer.Append(", ");
                writer.AppendAotStructPath(s);
                writer.AppendLine(">(ref host);");
            }
            else
            {
                // [WHY] The native value may be a reused ArrayPool shadow: a member that is not written (a null array
                // field) must read as empty, not as a device pointer that an earlier launch freed
                writer.AppendLine("native = default;");
                StructBlockCopyEmitter.WriteHostMirrorBinding(writer, s, hasMirror);
                foreach (var field in structLayout.DeviceFields)
                {
                    if (field.IsCopied)
                    {
                        WriteMarshalMember(writer, field.Member, dialect);
                    }
                }
            }
        }

        writer.AppendLine();
    }

    // public static void CopyArrays_<key>(ref <S> host)
    // { <host mirror binding>; if (host.<array> != null) { host.<array> = (<array type>)host.<array>.Clone();
    //   for (...) CopyArrays_<E>(ref host.<array>[i]); } CopyArrays_<nested>(ref host.<struct>);
    //   for (int k = 0; k < <N>; k++) { <the same for host[k]> } }

    private static void WriteMarshalMember(SourceWriter writer, StructAbiMember member, ITranspilerDialect dialect)
    {
        // GSH0078 reports the member; the shadow struct leaves it out
        if (NativeStructEmitter.IsUnrepresentableReference(member) ||
            StructBlockCopyEmitter.WriteBlockCopy(writer, member, toNative: true, dialect))
        {
            return;
        }

        if (member.Type.TryGetArrayLikeElementType(out var elementType))
        {
            var name = member.Name.ToCSharpIdentifier();
            WriteMarshalArrayMember(writer, $"host.{name}", $"native.{name}", elementType, dialect);
        }
        else if (TypeLayoutCalculator.IsUserStruct(member.Type))
        {
            writer.AppendMarshalMethodName(member.Type);
            using (writer.Parentheses())
            {
                writer.Append($"ref host.{member.Name.ToCSharpIdentifier()}, ref native.{member.Name.ToCSharpIdentifier()}, isDevicePtr, {StructArrayHelperEmitter.ArraysParameter(dialect)}, hStream");
            }

            writer.AppendLine(";");
        }
        else
        {
            if (member.Type.IsReferenceType || member.Type.SpecialType != SpecialType.None)
            {
                writer.AppendLine($"native.{member.Name.ToCSharpIdentifier()} = host.{member.Name.ToCSharpIdentifier()};");
            }
            else
            {
                writer.AppendLine($"fixed (byte* pField = native.{member.Name.ToCSharpIdentifier()})");
                using (writer.Scope())
                {
                    writer.Append("*((");
                    writer.AppendFullyQualifiedCsharpName(member.Type);
                    writer.AppendLine($"*)pField) = host.{member.Name.ToCSharpIdentifier()};");
                }
            }
        }
    }

    private const string ArrayProxy = "global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy";

    internal static void WriteMarshalArrayMember(SourceWriter writer, string host, string native,
        ITypeSymbol elementType, ITranspilerDialect dialect)
    {
        if (!dialect.IsCuda)
        {
            WriteCpuMarshalArrayMember(writer, host, native, elementType, dialect);
            return;
        }

        writer.AppendLine($"if ({host} != null)");
        using (writer.Scope())
        {
            writer.AppendLine("if (isDevicePtr)");
            using (writer.Scope())
            {
                writer.AppendLine(
                    "throw new global::System.NotSupportedException(\"Passing a struct with arrays is not supported when isDevicePtr is true.\");");
            }

            writer.AppendLine("else");
            using (writer.Scope())
            {
                StructBlockCopyEmitter.WriteArrayElementSize(writer, host, elementType);

                writer.AppendLine("if (size > 0)");
                using (writer.Scope())
                {
                    WriteSharedRegionLookup(writer, host, native);
                    writer.AppendLine("else");
                    using (writer.Scope())
                    {
                        WriteDeviceArrayCopy(writer, host, native, elementType);
                    }
                }

                // [WHY] An empty array is not null (#706): cuMemAlloc rejects zero bytes, so it gets a non-null address
                // that is never dereferenced, as Rust's NonNull::dangling() for an empty slice. It is not an allocation,
                // so it never enters the table of the launch (which frees what it holds); Unmarshal_ copies nothing back
                // for it (size 0)
                writer.AppendLine("else");
                using (writer.Scope())
                {
                    writer.AppendLine($"{native}.Data = {ArrayProxy}.EmptyArrayData;");
                }

                writer.AppendLine($"{native}.Length = {host}.Length;");
            }
        }
    }

    // if (deviceArrays != null && deviceArrays.TryGetRegion(<host>, out var region)) { <native>.Data = region; }
    // [WHY] In C# two fields that hold one array are one object, so the device must see one region too (#707): an array
    // that this launch already copied, through any field of any struct argument, gets that region and is not copied
    // again. Unmarshal_ then copies the same region back once per field, which writes the same bytes.
    private static void WriteSharedRegionLookup(SourceWriter writer, string host, string native)
    {
        writer.AppendLine(
            $"if ({StructMarshalingEmitter.CudaArraysParameter} != null && {StructMarshalingEmitter.CudaArraysParameter}.TryGetRegion({host}, out var region))");
        using (writer.Scope())
        {
            writer.AppendLine($"{native}.Data = region;");
        }
    }

    // cuMemAllocAsync(out <native>.Data, size); deviceArrays?.Add(<host>, <native>.Data); <copy the elements>
    // The region is recorded before the elements are marshaled, so an element that holds the same array finds it
    private static void WriteDeviceArrayCopy(SourceWriter writer, string host, string native, ITypeSymbol elementType)
    {
        writer.AppendLine(
            $"{HostTypeNames.CudaException}.ThrowIfError({HostTypeNames.CudaDriverApi}.cuMemAllocAsync(" +
            $"out {native}.Data, (global::System.IntPtr)size, hStream));");
        writer.AppendLine($"{StructMarshalingEmitter.CudaArraysParameter}?.Add({host}, {native}.Data);");

        if (!TypeLayoutCalculator.IsUserStruct(elementType))
        {
            StructBlockCopyEmitter.WritePinnedCopy(writer, host, $"{native}.Data", toDevice: true);
            return;
        }

        writer.Append("var tempArray = new ");
        writer.AppendAotStructPath(elementType);
        writer.AppendLine($"[{host}.Length];");

        writer.AppendLine($"for (int i = 0; i < {host}.Length; i++)");
        using (writer.Scope())
        {
            writer.AppendMarshalMethodName(elementType);
            writer.AppendLine(
                $"(ref {host}[i], ref tempArray[i], false, {StructMarshalingEmitter.CudaArraysParameter}, hStream);");
        }

        StructBlockCopyEmitter.WritePinnedCopy(writer, "tempArray", $"{native}.Data", toDevice: true);
    }


    // [WHY] The CPU kernel runs in this process, so an array field is pinned and passed as it is, with no copy: the
    // kernel writes the host array itself, and two fields that hold one array see one array, as in C# (design L5). An
    // element that lays out differently goes through a pinned shadow array. The launcher frees every handle in its
    // finally block; a missing list would leak the pins, so it throws before pinning anything.
    private static void WriteCpuMarshalArrayMember(SourceWriter writer, string host, string native,
        ITypeSymbol elementType, ITranspilerDialect dialect)
    {
        const string GcHandle = HostTypeNames.GCHandle;
        writer.AppendLine($"if ({host} != null)");
        using (writer.Scope())
        {
            writer.AppendLine(
                $"var pinnedHandles = {StructMarshalingEmitter.CpuArraysParameter} ?? throw new global::System.ArgumentNullException(nameof({StructMarshalingEmitter.CpuArraysParameter}));");
            var pinned = host;
            if (KernelParameterShape.IsConverted(elementType, dialect))
            {
                writer.Append("var shadow = new ");
                writer.AppendAotStructPath(elementType);
                writer.AppendLine($"[{host}.Length];");
                writer.AppendLine($"for (int i = 0; i < {host}.Length; i++)");
                using (writer.Scope())
                {
                    writer.AppendMarshalMethodName(elementType);
                    writer.AppendLine($"(ref {host}[i], ref shadow[i], false, {StructMarshalingEmitter.CpuArraysParameter}, hStream);");
                }

                pinned = "shadow";
            }

            writer.AppendLine(
                $"var handle = {GcHandle}.Alloc({pinned}, {HostTypeNames.GCHandleType}.Pinned);");
            writer.AppendLine($"pinnedHandles.Add({GcHandle}.ToIntPtr(handle));");
            writer.AppendLine($"{native}.Data = handle.AddrOfPinnedObject();");
            writer.AppendLine($"{native}.Length = {host}.Length;");
        }
    }

    // The kernel wrote the pinned host array itself, so only a shadow array of converted elements is copied back:
    // if (<host> != null && <native>.Data != IntPtr.Zero) { var shadow = (<element shadow>*)<native>.Data;
    // for (...) Unmarshal_<element>(ref <host>[i], ref shadow[i], false, hStream); }
    // The shadow stays pinned until the launcher's finally block, after the unmarshaling.
}
