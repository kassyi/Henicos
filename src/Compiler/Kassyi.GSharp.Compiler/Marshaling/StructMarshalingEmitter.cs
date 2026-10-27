namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits custom struct marshal and unmarshal helper methods between managed and native shadow representations.</summary>
internal static class StructMarshalingEmitter
{
    private static readonly ITranspilerDialect s_cudaDialect = new GSharpCudaDialect();
    private static readonly ITranspilerDialect s_cpuDialect = new StandardCppDialect();

    /// <summary>Emits the rendered structs of a kernel class (<see cref="RenderStructs"/>).</summary>
    /// <remarks>A struct whose <c>AotContexts</c> name is already taken gets no helpers; GSH0058 reports it.</remarks>
    internal static void WriteStructMarshaling(SourceWriter writer, EquatableArray<StructHostSource> structs, bool isCuda)
    {
        if (structs.Length > 0)
        {
            writer.AppendLine(
                "#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type");
            foreach (var s in structs)
            {
                writer.Append(s.GetText(isCuda));
            }

            writer.AppendLine("#pragma warning restore CS8500");
        }
    }

    /// <summary>
    /// Renders the native shadow struct and the marshaling methods of each custom struct of <paramref name="kernel"/>'s
    /// parameters, one per <c>AotContexts</c> name (<see cref="CollectUniqueStructs"/>).
    /// </summary>
    /// <remarks>
    /// The element of an array that only the CUDA launcher converts has no CPU text: the CPU launcher passes the array
    /// as it is (until L4).
    /// </remarks>
    internal static EquatableArray<StructHostSource> RenderStructs(IMethodSymbol kernel)
    {
        var structTypes = CollectUniqueStructs([kernel], null);
        var cpuStructs = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        kernel.Parameters
            .AsStackEnumerable()
            .Do(
                cpuStructs,
                static (parameter, structs) => TypeLayoutCalculator.CollectStructs(parameter.Type, structs)
            );

        var sources = ImmutableArray.CreateBuilder<StructHostSource>(structTypes.Length);
        foreach (var s in structTypes)
        {
            sources.Add(new StructHostSource(
                AotHostNames.GetAotStructPath(s),
                RenderStructForDialect(s, s_cudaDialect),
                cpuStructs.Contains(s) ? RenderStructForDialect(s, s_cpuDialect) : string.Empty));
        }

        return sources.MoveToImmutable().AsEquatableArray();
    }

    private static string RenderStructForDialect(INamedTypeSymbol s, ITranspilerDialect dialect)
    {
        var structLayout = TypeLayoutCalculator.GetStructLayout(s, dialect);
        using var writer = new SourceWriter();
        NativeStructEmitter.WriteNativeStructDeclaration(writer, s, structLayout);
        WriteMarshalMethod(writer, s, structLayout, dialect);
        WriteUnmarshalMethod(writer, s, structLayout, dialect);
        return writer.ToString();
    }

    /// <summary>
    /// Collects the custom structs of the kernel signatures, one per <c>AotContexts</c> name. A struct that maps to a
    /// name that another struct already has is skipped and, when <paramref name="duplicates"/> is given, recorded.
    /// </summary>
    /// <remarks>
    /// Two constructions of one generic struct (<c>Box&lt;int&gt;</c>, <c>Box&lt;long&gt;</c>) are also duplicates:
    /// the native declaration is not generic, so each construction needs its own layout under the same name.
    /// </remarks>
    internal static ImmutableArray<INamedTypeSymbol> CollectUniqueStructs(IEnumerable<IMethodSymbol> kernels,
        ImmutableArray<(INamedTypeSymbol Kept, INamedTypeSymbol Duplicate, string Name)>.Builder? duplicates)
    {
        var structTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var kernel in kernels)
        {
            kernel.Parameters
                .AsStackEnumerable()
                .Do(structTypes, static (parameter, structs) => CollectParameterStructs(parameter.Type, structs));
        }

        var byName = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        var unique = ImmutableArray.CreateBuilder<INamedTypeSymbol>(structTypes.Count);
        foreach (var s in structTypes)
        {
            var name = AotHostNames.GetAotStructPath(s);
            if (!byName.TryGetValue(name, out var kept))
            {
                byName.Add(name, s);
                unique.Add(s);
            }
            else
            {
                duplicates?.Add((kept, s, name));
            }
        }

        return unique.ToImmutable();
    }

    // An array-like parameter whose element the CUDA launcher converts goes through the element's shadow
    private static void CollectParameterStructs(ITypeSymbol type, HashSet<INamedTypeSymbol> structs)
    {
        var marshaledType = type.UnwrapNullableArrayLike() ?? type;
        TypeLayoutCalculator.CollectStructs(
            marshaledType.TryGetArrayLikeElementType(out var element) && KernelParameterShape.IsConvertedOnCuda(element)
                ? element
                : type,
            structs);
    }

    private static void WriteMarshalMethod(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout,
        ITranspilerDialect dialect)
    {
        var hasMirror = NativeStructEmitter.NeedsHostMirror(s, structLayout);
        writer.Append("public static unsafe void ");
        writer.AppendMarshalMethodName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(hasMirror ? $" {MirroredHostParameter}, ref " : " host, ref ");
            writer.AppendAotStructPath(s);
            writer.Append(
                " native, bool isDevicePtr, System.Collections.Generic.List<IntPtr>? allocatedPtrs, IntPtr hStream = default");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            if (structLayout.Transfer == TransferClass.Identical)
            {
                writer.Append("native = System.Runtime.CompilerServices.Unsafe.As<");
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
                WriteHostMirrorBinding(writer, s, hasMirror);
                foreach (var member in StructAbiMembers.Get(s))
                {
                    WriteMarshalMember(writer, member, dialect);
                }
            }
        }

        writer.AppendLine();
    }

    // The host value of a struct with a host mirror arrives under this name, and `host` is then the mirror
    private const string MirroredHostParameter = "hostValue";

    // if (Unsafe.SizeOf<S>() != Unsafe.SizeOf<Mirror>()) LayoutGuard.ThrowSizeMismatch(...);
    // if (!Mirror.FieldOffsetsMatch) LayoutGuard.ThrowFieldOffsetMismatch(...);
    // ref var host = ref Unsafe.As<S, Mirror>(ref hostValue);
    // [WHY] The members are then read and written by the names of the mirror, whose fields name the hidden fields of
    // the captured primary constructor parameters (NativeStructEmitter.NeedsHostMirror). The size and offset checks
    // catch a mirror that does not have the CLR layout of S.
    private static void WriteHostMirrorBinding(SourceWriter writer, INamedTypeSymbol s, bool hasMirror)
    {
        if (!hasMirror)
        {
            return;
        }

        writer.Append("if (System.Runtime.CompilerServices.Unsafe.SizeOf<");
        writer.AppendFullyQualifiedCsharpName(s);
        writer.Append(">() != System.Runtime.CompilerServices.Unsafe.SizeOf<");
        writer.AppendAotStructPath(s);
        writer.Append($".{NativeStructEmitter.HostMirrorName}>()) ");
        writer.Append("global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowSizeMismatch(\"");
        writer.Append(TypeLayoutCalculator.FormatTypeName(s));
        writer.Append("\", System.Runtime.CompilerServices.Unsafe.SizeOf<");
        writer.AppendAotStructPath(s);
        writer.Append($".{NativeStructEmitter.HostMirrorName}>(), System.Runtime.CompilerServices.Unsafe.SizeOf<");
        writer.AppendFullyQualifiedCsharpName(s);
        writer.AppendLine(">());");
        writer.Append("if (!");
        writer.AppendAotStructPath(s);
        writer.Append($".{NativeStructEmitter.HostMirrorName}.{NativeStructEmitter.HostMirrorOffsetsMatch}) ");
        writer.Append("global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch(\"");
        writer.Append(TypeLayoutCalculator.FormatTypeName(s));
        writer.AppendLine("\");");
        writer.Append("ref var host = ref System.Runtime.CompilerServices.Unsafe.As<");
        writer.AppendFullyQualifiedCsharpName(s);
        writer.Append(", ");
        writer.AppendAotStructPath(s);
        writer.AppendLine($".{NativeStructEmitter.HostMirrorName}>(ref {MirroredHostParameter});");
    }

    // [WHY] An [InlineArray(N)] struct and a fixed buffer are copied as one block of ElementCount elements: the element
    // field of an inline array is usually private, and a fixed buffer cannot be assigned. The inline array's host value
    // is the whole struct, so it is copied by its own address (Unsafe.Copy infers its type from host). An inline array
    // whose element lays out differently on the device, or holds arrays, is copied element by element instead
    // (host[k] is the element, whatever the field's accessibility).
    private static bool WriteBlockCopy(SourceWriter writer, StructAbiMember member, bool toNative,
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
                            ? "System.Runtime.CompilerServices.Unsafe.Copy(pField, ref host);"
                            : "System.Runtime.CompilerServices.Unsafe.Copy(ref host, pField);");
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
                        writer.AppendLine($"System.Buffer.MemoryCopy({(toNative ? "pHost, pField" : "pField, pHost")}, {byteCount}, {byteCount});");
                    }
                }

                return true;
            default:
                return false;
        }
    }

    // Whether an inline array element is copied one by one: a user struct that is not Identical, or an array-like value
    // (a GSharpArray on the device, a reference on the host)
    private static bool NeedsElementCopy(ITypeSymbol elementType, ITranspilerDialect dialect) =>
        elementType.IsArrayLikeType() ||
        elementType is INamedTypeSymbol named &&
        TypeLayoutCalculator.IsUserStruct(named) &&
        TypeLayoutCalculator.GetStructLayout(named, dialect).Transfer != TransferClass.Identical;

    // for (int k = 0; k < <N>; k++) { <copy host[k] and ((<element shadow>*)pField)[k]> }
    private static void WriteInlineArrayElementCopy(SourceWriter writer, StructAbiMember member, bool toNative,
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
                    WriteMarshalArrayMember(writer, "host[k]", NativeElement, arrayElementType, dialect);
                }
                else
                {
                    WriteUnmarshalArrayMember(writer, "host[k]", NativeElement, arrayElementType, dialect);
                }

                return;
            }

            var shadow = AotHostNames.GetAotStructPath(member.Type);
            if (toNative)
            {
                writer.AppendMarshalMethodName(member.Type);
                writer.AppendLine($"(ref host[k], ref (({shadow}*)pField)[k], isDevicePtr, allocatedPtrs, hStream);");
            }
            else
            {
                writer.AppendUnmarshalMethodName(member.Type);
                writer.AppendLine($"(ref host[k], ref (({shadow}*)pField)[k], isDevicePtr, hStream);");
            }
        }
    }

    private static void WriteMarshalMember(SourceWriter writer, StructAbiMember member, ITranspilerDialect dialect)
    {
        // GSH0078 reports the member; the shadow struct leaves it out
        if (NativeStructEmitter.IsUnrepresentableReference(member) ||
            WriteBlockCopy(writer, member, toNative: true, dialect))
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
                writer.Append($"ref host.{member.Name.ToCSharpIdentifier()}, ref native.{member.Name.ToCSharpIdentifier()}, isDevicePtr, allocatedPtrs, hStream");
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

    private static void WriteMarshalArrayMember(SourceWriter writer, string host, string native,
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
                    "throw new System.NotSupportedException(\"Passing a struct with arrays is not supported when isDevicePtr is true.\");");
            }

            writer.AppendLine("else");
            using (writer.Scope())
            {
                WriteArrayElementSize(writer, host, elementType);

                writer.AppendLine("if (size > 0)");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        "CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(" +
                        $"out {native}.Data, (IntPtr)size, hStream));");
                    writer.AppendLine($"allocatedPtrs?.Add({native}.Data);");

                    if (TypeLayoutCalculator.IsUserStruct(elementType))
                    {
                        writer.Append("var tempArray = new ");
                        writer.AppendAotStructPath(elementType);
                        writer.AppendLine($"[{host}.Length];");

                        writer.AppendLine($"for (int i = 0; i < {host}.Length; i++)");
                        using (writer.Scope())
                        {
                            writer.AppendMarshalMethodName(elementType);
                            writer.AppendLine(
                                $"(ref {host}[i], ref tempArray[i], false, allocatedPtrs, hStream);");
                        }

                        writer.AppendLine(
                            "var handle = System.Runtime.InteropServices.GCHandle.Alloc(tempArray, System.Runtime.InteropServices.GCHandleType.Pinned);");
                        writer.AppendLine("try");
                        using (writer.Scope())
                        {
                            writer.AppendLine(
                                "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(" +
                                $"{native}.Data, handle.AddrOfPinnedObject(), (IntPtr)size, hStream));");
                        }

                        writer.AppendLine("finally");
                        using (writer.Scope())
                        {
                            writer.AppendLine("handle.Free();");
                        }
                    }
                    else
                    {
                        writer.AppendLine(
                            $"var handle = System.Runtime.InteropServices.GCHandle.Alloc({host}, " +
                            "System.Runtime.InteropServices.GCHandleType.Pinned);");
                        writer.AppendLine("try");
                        using (writer.Scope())
                        {
                            writer.AppendLine(
                                "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(" +
                                $"{native}.Data, handle.AddrOfPinnedObject(), (IntPtr)size, hStream));");
                        }

                        writer.AppendLine("finally");
                        using (writer.Scope())
                        {
                            writer.AppendLine("handle.Free();");
                        }
                    }
                }

                writer.AppendLine($"{native}.Length = {host}.Length;");
            }
        }
    }

    private static void WriteUnmarshalMethod(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout,
        ITranspilerDialect dialect)
    {
        var hasMirror = NativeStructEmitter.NeedsHostMirror(s, structLayout);
        writer.Append("public static unsafe void ");
        writer.AppendUnmarshalMethodName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(hasMirror ? $" {MirroredHostParameter}, ref " : " host, ref ");
            writer.AppendAotStructPath(s);
            writer.Append(" native, bool isDevicePtr, IntPtr hStream = default");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            if (structLayout.Transfer == TransferClass.Identical)
            {
                writer.Append("host = System.Runtime.CompilerServices.Unsafe.As<");
                writer.AppendAotStructPath(s);
                writer.Append(", ");
                writer.AppendFullyQualifiedCsharpName(s);
                writer.AppendLine(">(ref native);");
            }
            else
            {
                WriteHostMirrorBinding(writer, s, hasMirror);
                foreach (var member in StructAbiMembers.Get(s))
                {
                    WriteUnmarshalMember(writer, member, dialect);
                }
            }
        }

        writer.AppendLine();
    }

    private static void WriteUnmarshalMember(SourceWriter writer, StructAbiMember member, ITranspilerDialect dialect)
    {
        if (NativeStructEmitter.IsUnrepresentableReference(member) ||
            WriteBlockCopy(writer, member, toNative: false, dialect))
        {
            return;
        }

        if (member.Type.TryGetArrayLikeElementType(out var elementType))
        {
            var name = member.Name.ToCSharpIdentifier();
            WriteUnmarshalArrayMember(writer, $"host.{name}", $"native.{name}", elementType, dialect);
        }
        else if (TypeLayoutCalculator.IsUserStruct(member.Type))
        {
            writer.AppendUnmarshalMethodName(member.Type);
            using (writer.Parentheses())
            {
                writer.Append($"ref host.{member.Name.ToCSharpIdentifier()}, ref native.{member.Name.ToCSharpIdentifier()}, isDevicePtr, hStream");
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

    private static void WriteUnmarshalArrayMember(SourceWriter writer, string host, string native,
        ITypeSymbol elementType, ITranspilerDialect dialect)
    {
        if (!dialect.IsCuda)
        {
            WriteCpuUnmarshalArrayMember(writer, host, native, elementType, dialect);
            return;
        }

        writer.AppendLine(
            $"if ({host} != null && !isDevicePtr && {native}.Data != IntPtr.Zero)");
        using (writer.Scope())
        {
            WriteArrayElementSize(writer, host, elementType);

            writer.AppendLine("if (size > 0)");
            using (writer.Scope())
            {
                if (TypeLayoutCalculator.IsUserStruct(elementType))
                {
                    writer.Append("var tempArray = new ");
                    writer.AppendAotStructPath(elementType);
                    writer.AppendLine($"[{host}.Length];");

                    writer.AppendLine(
                        "var handle = System.Runtime.InteropServices.GCHandle.Alloc(tempArray, System.Runtime.InteropServices.GCHandleType.Pinned);");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.AppendLine(
                            "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                                $"handle.AddrOfPinnedObject(), {native}.Data, (IntPtr)size, hStream));");
                    }

                    writer.AppendLine("finally");
                    using (writer.Scope())
                    {
                        writer.AppendLine("handle.Free();");
                    }

                    writer.AppendLine($"for (int i = 0; i < {host}.Length; i++)");
                    using (writer.Scope())
                    {
                        writer.AppendUnmarshalMethodName(elementType);
                        writer.AppendLine($"(ref {host}[i], ref tempArray[i], false, hStream);");
                    }
                }
                else
                {
                    writer.AppendLine(
                        $"var handle = System.Runtime.InteropServices.GCHandle.Alloc({host}, " +
                            "System.Runtime.InteropServices.GCHandleType.Pinned);");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.AppendLine(
                            "CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2(" +
                                $"handle.AddrOfPinnedObject(), {native}.Data, (IntPtr)size, hStream));");
                    }

                    writer.AppendLine("finally");
                    using (writer.Scope())
                    {
                        writer.AppendLine("handle.Free();");
                    }
                }
            }
        }
    }

    // if (<host> != null) {
    //   var pinnedHandles = allocatedPtrs ?? throw new ArgumentNullException(nameof(allocatedPtrs));
    //   [var shadow = new <element shadow>[<host>.Length]; for (...) Marshal_<element>(ref <host>[i], ref shadow[i], ...);]
    //   var handle = GCHandle.Alloc(<host or shadow>, GCHandleType.Pinned); pinnedHandles.Add(GCHandle.ToIntPtr(handle));
    //   <native>.Data = handle.AddrOfPinnedObject(); <native>.Length = <host>.Length; }
    // [WHY] The CPU kernel runs in this process, so an array field is pinned and passed as it is, with no copy: the
    // kernel writes the host array itself, and two fields that hold one array see one array, as in C# (design L5). An
    // element that lays out differently goes through a pinned shadow array. The launcher frees every handle in its
    // finally block; a missing list would leak the pins, so it throws before pinning anything.
    private static void WriteCpuMarshalArrayMember(SourceWriter writer, string host, string native,
        ITypeSymbol elementType, ITranspilerDialect dialect)
    {
        const string GcHandle = "System.Runtime.InteropServices.GCHandle";
        writer.AppendLine($"if ({host} != null)");
        using (writer.Scope())
        {
            writer.AppendLine(
                "var pinnedHandles = allocatedPtrs ?? throw new System.ArgumentNullException(nameof(allocatedPtrs));");
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
                    writer.AppendLine($"(ref {host}[i], ref shadow[i], false, allocatedPtrs, hStream);");
                }

                pinned = "shadow";
            }

            writer.AppendLine(
                $"var handle = {GcHandle}.Alloc({pinned}, System.Runtime.InteropServices.GCHandleType.Pinned);");
            writer.AppendLine($"pinnedHandles.Add({GcHandle}.ToIntPtr(handle));");
            writer.AppendLine($"{native}.Data = handle.AddrOfPinnedObject();");
            writer.AppendLine($"{native}.Length = {host}.Length;");
        }
    }

    // The kernel wrote the pinned host array itself, so only a shadow array of converted elements is copied back:
    // if (<host> != null && <native>.Data != IntPtr.Zero) { var shadow = (<element shadow>*)<native>.Data;
    // for (...) Unmarshal_<element>(ref <host>[i], ref shadow[i], false, hStream); }
    // The shadow stays pinned until the launcher's finally block, after the unmarshaling.
    private static void WriteCpuUnmarshalArrayMember(SourceWriter writer, string host, string native,
        ITypeSymbol elementType, ITranspilerDialect dialect)
    {
        if (!KernelParameterShape.IsConverted(elementType, dialect))
        {
            return;
        }

        writer.AppendLine($"if ({host} != null && {native}.Data != IntPtr.Zero)");
        using (writer.Scope())
        {
            writer.Append("var shadow = (");
            writer.AppendAotStructPath(elementType);
            writer.AppendLine($"*){native}.Data;");
            writer.AppendLine($"for (int i = 0; i < {host}.Length; i++)");
            using (writer.Scope())
            {
                writer.AppendUnmarshalMethodName(elementType);
                writer.AppendLine($"(ref {host}[i], ref shadow[i], false, hStream);");
            }
        }
    }

    private static void WriteArrayElementSize(SourceWriter writer, string host, ITypeSymbol elementType)
    {
        writer.Append($"int size = {host}.Length * sizeof(");
        if (TypeLayoutCalculator.IsUserStruct(elementType))
        {
            writer.AppendAotStructPath(elementType);
        }
        else
        {
            writer.AppendFullyQualifiedCsharpName(elementType);
        }

        writer.AppendLine(");");
    }
}
