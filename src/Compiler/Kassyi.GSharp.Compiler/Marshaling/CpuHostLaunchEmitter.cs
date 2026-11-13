namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the CPU host launch members of a kernel wrapper class: delegate, the copies of <c>[In]</c> arrays
/// (<see cref="CpuArrayCopyEmitter"/>), managed fallback (<see cref="ManagedFallbackEmitter"/>) and native call.
/// </summary>
internal static class CpuHostLaunchEmitter
{
    private const string ReferenceEqualsMethod = ArraySharingPlan.ReferenceEqualsMethod;
    private const string ArchSuffix = GSharpInternalNames.Launcher.ArchSuffix;
    private const string DllName = GSharpInternalNames.Launcher.DllName;
    private const string DllPath = GSharpInternalNames.Launcher.DllPath;
    private const string FunctionHandle = GSharpInternalNames.Launcher.FunctionHandle;
    private const string PinnedHandle = GSharpInternalNames.Launcher.PinnedHandle;
    private const string GridBlockCount = GSharpInternalNames.Launcher.GridBlockCount;
    private const string ArrayPinningType = HostTypeNames.ArrayPinning;

    /// <summary>Emits the managed source, the delegate declaration and the launch wrapper of one kernel.</summary>
    public static void WriteKernelMembers(SourceWriter writer, in KernelEmission kernel, Accessibility accessibility,
        string moduleName)
    {
        writer.AppendLine(kernel.ManagedCSharpSource);
        WrapperEmitter.WriteLayoutCheckedField(writer, kernel.Host.CpuLayoutGuards, kernel.MemberKey);
        WriteCpuDelegateDeclaration(writer, kernel);
        WriteLaunchMethodCpu(writer, kernel, accessibility, moduleName);
    }

    private static void WriteLaunchMethodCpu(SourceWriter writer, in KernelEmission kernel, Accessibility accessibility,
        string moduleName)
    {
        using var suppression = writer.WarningSuppressed(ManagedPointerWarning.Id);
        WrapperEmitter.WriteLaunchMethodSignature(writer, kernel, false, accessibility);
        using (writer.Scope())
        {
            WrapperEmitter.WriteOutParameterDefaults(writer, kernel.Host);
            LaunchLimitEmitter.WriteCpuCheck(writer, kernel.Host);
            AutoResultEmitter.WriteAllocation(writer, kernel.Host);
            CSharpExecutionEmitter.WriteBranch(writer, CSharpExecutionPlan.Create(kernel.Host, isDevicePtr: false));
            var sharing = ArraySharingPlan.ForCpu(kernel.Host);
            using (AliasGroupEmitter.WriteCpuGroups(writer, sharing))
            {
                CpuArrayCopyEmitter.WriteKernelCopies(writer, sharing);
            }

            WriteDllPath(writer, moduleName);
            ManagedFallbackEmitter.WriteManagedExecutionFallback(writer, kernel.Host);

            // [WHY] Only the native launcher reads the host bytes as the C++ layout; the managed fallback uses the values
            WrapperEmitter.WriteLayoutGuards(writer, kernel.Host.CpuLayoutGuards, kernel.MemberKey);
            WriteCpuDelegateGetOrCompile(writer, kernel);
            WriteCpuFixedBlocksAndInvoke(writer, kernel.Host, sharing);
        }
    }

    private static void WriteCpuDelegateDeclaration(SourceWriter writer, in KernelEmission kernel)
    {
        // Generate Delegate
        writer.AppendLine(
            "[global::System.Runtime.InteropServices.UnmanagedFunctionPointer(global::System.Runtime.InteropServices.CallingConvention.Cdecl, CharSet = global::System.Runtime.InteropServices.CharSet.Unicode)]");
        writer.Append("private unsafe delegate void ");
        writer.Append(KernelHostNames.GetDelegateTypeName(kernel));
        using (writer.Parentheses())
        {
            writer.Append("uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY, uint blockDimZ, ");
            writer.Append($"uint sharedMemBytes, long {GridBlockCount}");
            foreach (var p in kernel.Host.Parameters)
            {
                writer.Append(", ");

                if (p is { IsArray: true, IsNullableValue: true })
                {
                    writer.Append($"global::System.Nullable<{HostTypeNames.ArrayProxy}>* {p.Identifier}");
                }
                else if (p.IsArray)
                {
                    writer.Append($"global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {p.Identifier}");
                }
                else
                {
                    // [WHY] A user struct by value goes as a pointer to the host value, so that the runtime marshaller
                    // never converts it (bool, char, the x64 size rules); the native launcher copies it (#511). A struct
                    // whose layouts differ goes as a pointer to its native shadow struct.
                    var typeName = p.IsCpuConvertedStruct && p.AotStructPath is { } shadow ? shadow : p.TypeName;
                    writer.Append($"{typeName}{(p.IsByRef || p.IsCpuValueStruct ? "*" : "")} {p.Identifier}");
                }
            }

            AutoResultEmitter.WriteCpuDelegateParameter(writer, kernel.Host);
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    // string __gsharp_dll_path = <the native library of the module for this architecture>;
    private static void WriteDllPath(SourceWriter writer, string moduleName)
    {
        var fileStem = AotModuleNames.GetArtifactFileStem(moduleName);
        writer.AppendLine(
            $"string {ArchSuffix} = global::System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==" +
            $" global::System.Runtime.InteropServices.Architecture.Arm64 ? \"_arm64\" : \"_x64\";");
        // [WHY] The name goes into an interpolated string: a quote or backslash is escaped, a brace doubled
        writer.Append($"string {DllName} = $\"");
        AppendInterpolatedStringText(writer, fileStem);
        writer.AppendLine($"{{{ArchSuffix}}}.dll\";");
        writer.AppendLine(
            $"string {DllPath} = global::System.IO.Path.Combine(global::System.AppContext.BaseDirectory, {DllName});");
    }

    // <text> as the text of a regular interpolated string: escaped as a string literal, each brace doubled.
    // [WHY] FormatLiteral escapes a double quote only when it writes the quotes, so the quoted form is written without
    // its first and last character
    private static void AppendInterpolatedStringText(SourceWriter writer, string text)
    {
        var literal = SymbolDisplay.FormatLiteral(text, quote: true);
        for (var i = 1; i < literal.Length - 1; i++)
        {
            var c = literal[i];
            writer.Append(c);
            writer.AppendIf(c == '{', "{").AppendIf(c == '}', "}");
        }
    }

    private static void WriteCpuDelegateGetOrCompile(SourceWriter writer, in KernelEmission kernel)
    {
        var exportName = kernel.Host.LaunchExportName;

        var delegateType = KernelHostNames.GetDelegateTypeName(kernel);
        writer.Append($"{delegateType} {FunctionHandle} = ");
        writer.Append($"global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.LoadAotKernel<{delegateType}>");
        writer.AppendLine($"(\"{exportName}\", {DllPath});");
        if (!kernel.ErrorSites.IsDefaultOrEmpty)
        {
            writer.AppendLine("if (s_errorSites.Length > 0)");
            using (writer.Scope())
            {
                writer.AppendLine($"global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.RegisterErrorSites({DllPath}, s_errorSites);");
            }
        }
    }

    // <declarations>; try { <marshaling>; <fixed blocks and call>; <unmarshaling> } finally { <release> }
    // [WHY] Marshal_ pins the array fields of a converted struct or element and records each handle; this one finally
    // block frees them and returns the rented shadows after the call and the copy back, also when the marshaling, the
    // native call or its error check throws.
    private static void WriteCpuFixedBlocksAndInvoke(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan sharing)
    {
        var parameters = kernel.Parameters;
        var hasConverted = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            hasConverted |= TryWriteConvertedDeclaration(writer, sharing, i);
        }

        hasConverted |= AutoResultEmitter.TryWriteCpuConvertedDeclaration(writer, kernel);
        AliasGroupEmitter.WriteCpuHandlesDeclaration(writer, sharing.Aliases);

        if (!hasConverted)
        {
            WriteFixedBlocksAndInvoke(writer, kernel, sharing);
            AutoResultEmitter.WriteReturn(writer, kernel);
            return;
        }

        writer.AppendLine("try");
        using (writer.Scope())
        {
            AliasGroupEmitter.WriteCpuConversions(writer, sharing.Aliases, toDevice: true);

            // [WHY] The same order as the CUDA launcher: an [Out] array that shares a shadow comes after the holders
            // that convert their elements into it (#721)
            for (var i = 0; i < parameters.Length; i++)
            {
                if (!sharing.IsMarshaledLast(i))
                {
                    WriteConvertedMarshal(writer, sharing, i);
                }
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                if (sharing.IsMarshaledLast(i))
                {
                    WriteConvertedMarshal(writer, sharing, i);
                }
            }

            AutoResultEmitter.WriteCpuConvertedMarshal(writer, kernel);
            WriteFixedBlocksAndInvoke(writer, kernel, sharing);
            for (var i = 0; i < parameters.Length; i++)
            {
                WriteConvertedUnmarshal(writer, sharing, i);
            }

            AliasGroupEmitter.WriteCpuConversions(writer, sharing.Aliases, toDevice: false);
            AutoResultEmitter.WriteCpuConvertedUnmarshal(writer, kernel);
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                WriteConvertedRelease(writer, sharing, i);
            }

            AliasGroupEmitter.WriteCpuHandlesRelease(writer, sharing.Aliases);

            AutoResultEmitter.WriteCpuConvertedRelease(writer, kernel);
        }

        AutoResultEmitter.WriteReturn(writer, kernel);
    }

    // Converted struct: <shadow> __gsharp_native_<ord> = default; List<IntPtr> __gsharp_allocated_ptrs_<ord> = new();
    // Converted array: byte[]? __gsharp_shadow_<ord> = null; List<IntPtr> __gsharp_allocated_ptrs_<ord> = new();
    //   int __gsharp_size_<ord> = checked(...);
    // byte[]? __gsharp_shared_shadow_<ord> = null;                         (a converted T[] that an earlier holder may share)
    // Returns whether it wrote the declarations of a converted parameter. The size is computed before the try block,
    // when nothing is allocated.
    private static bool TryWriteConvertedDeclaration(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        writer.AppendLineIf(sharing.HasShadowSource(index), $"byte[]? {KernelHostNames.GetLocal(LauncherLocal.SharedShadow, shape)} = null;");
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, AotStructPath: { } shadow }:
                writer.AppendLine($"{shadow} {KernelHostNames.GetLocal(LauncherLocal.Native, shape)} = default;");
                WriteAllocatedPointerList(writer, shape);
                return true;
            case { IsCpuConvertedArray: true, Cpu.ConvertedElementAotStructPath: { } element }:
                writer.AppendLine($"byte[]? {KernelHostNames.GetLocal(LauncherLocal.Shadow, shape)} = null;");
                WriteAllocatedPointerList(writer, shape);
                writer.AppendLine($"int {KernelHostNames.GetLocal(LauncherLocal.Size, shape)} = checked({shape.LengthExpression} * sizeof({element}));");
                return true;
            default:
                return false;
        }
    }

    // Marshal_<S>(ref <name>, ref __gsharp_native_<ord>, false, __gsharp_allocated_ptrs_<ord>, hStream);                (converted struct)
    // __gsharp_shadow_<ord> = ArrayPool<byte>.Shared.Rent(__gsharp_size_<ord>); <spans>;                                (converted array)
    // for (...) Marshal_<E>(ref __gsharp_host_<ord>[i], ref __gsharp_native_<ord>[i], false, __gsharp_allocated_ptrs_<ord>, hStream); ([In] / in-out)
    // __gsharp_native_<ord>.Clear();                                       ([Out] of an element without array fields)
    // [WHY] The elements of an array differ in layout, so they are converted into a rented shadow array of the device
    // layout, which the kernel reads in place (the CUDA launcher copies the same shadow to the device). An [Out] element
    // with array fields is still marshaled: the kernel writes into its pinned arrays. A rented array holds the bytes of
    // an earlier use, so an [Out] shadow is cleared rather than left to the kernel.
    // A converted T[] that holds the array of a holder marshaled before it takes that shadow and fills nothing
    // (WriteSharedShadowLookup).
    private static void WriteConvertedMarshal(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, MarshalMethodName: { } marshal }:
                writer.AppendLine(
                    $"{marshal}(ref {shape.Identifier}, ref {KernelHostNames.GetLocal(LauncherLocal.Native, shape)}, {KernelHostNames.GetLocal(LauncherLocal.AllocatedPointers, shape)}, hStream);");
                break;
            case { IsCpuConvertedArray: true } when sharing.HasShadowSource(index):
                var sharedShadow = KernelHostNames.GetLocal(LauncherLocal.SharedShadow, shape);
                WriteSharedShadowLookup(writer, sharing, index);
                writer.AppendLine(
                    $"{KernelHostNames.GetLocal(LauncherLocal.Shadow, shape)} = {sharedShadow} ?? global::System.Buffers.ArrayPool<byte>.Shared.Rent({KernelHostNames.GetLocal(LauncherLocal.Size, shape)});");
                ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cpu);
                writer.AppendLine($"if ({sharedShadow} == null)");
                using (writer.Scope())
                {
                    WriteConvertedElements(writer, sharing, index);
                }

                break;
            case { IsCpuConvertedArray: true }:
                writer.AppendLine($"{KernelHostNames.GetLocal(LauncherLocal.Shadow, shape)} = global::System.Buffers.ArrayPool<byte>.Shared.Rent({KernelHostNames.GetLocal(LauncherLocal.Size, shape)});");
                ConvertedArrayEmitter.WriteShadowAndHostSpans(writer, shape, shape.Cpu);
                WriteConvertedElements(writer, sharing, index);
                break;
        }
    }

    // An [Out] array of elements without array fields is cleared instead of converted (ArraySharingPlan.CopiesIn)
    private static void WriteConvertedElements(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        if (!sharing.CopiesIn(index))
        {
            writer.AppendLine($"{KernelHostNames.GetLocal(LauncherLocal.Native, shape)}.Clear();");
        }
        else
        {
            ConvertedArrayEmitter.WriteElementMarshalLoop(writer, shape, shape.Cpu, KernelHostNames.GetLocal(LauncherLocal.AllocatedPointers, shape));
        }
    }

    // __gsharp_shared_shadow_<ord> = ReferenceEquals(<name>, <earlier>) ? __gsharp_shadow_<earlier> : ... : null;
    // [WHY] Converted arguments that hold one array share one shadow, which the native kernel reads and writes in place,
    // as the CUDA launcher gives them one device region (#721). The first holder converts the elements into it; an [Out]
    // holder comes last, so that it never clears what an earlier holder converted. Every holder that copies back reads
    // the same shadow, so the host elements get the same values.
    private static void WriteSharedShadowLookup(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        writer.Append($"{KernelHostNames.GetLocal(LauncherLocal.SharedShadow, shape)} = ");
        for (var k = 0; k < sharing.Parameters.Length; k++)
        {
            if (sharing.IsShadowSource(index, k))
            {
                var source = sharing.Parameters[k];
                writer.Append(
                    $"{ReferenceEqualsMethod}({shape.Identifier}, {source.Identifier}) ? {KernelHostNames.GetLocal(LauncherLocal.Shadow, source)} : ");
            }
        }

        writer.AppendLine("null;");
    }

    // Unmarshal_<S>(ref <name>, ref __gsharp_native_<ord>, hStream);                                     (converted struct)
    // for (...) Unmarshal_<E>(ref __gsharp_host_<ord>[i], ref __gsharp_native_<ord>[i], hStream);  (converted array, copied back)
    // The array goes through the spans that the marshaling declared in the same try block.
    // [WHY] A struct by value is unmarshaled as well: its own fields go back only to Launch's copy, but an array field
    // whose elements are converted shares the caller's array, whose elements must get the kernel's writes
    // A member of a group of converted elements is unmarshaled with its group (AliasGroupEmitter.WriteCpuConversions)
    private static void WriteConvertedUnmarshal(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        var member = AliasGroupEmitter.CpuBufferPointer(sharing.Aliases, index);
        var unmarshals = shape.IsCpuConvertedStruct ||
            shape is { IsCpuConvertedArray: true, IsCopiedOut: true, IsReadOnlyContainer: false };
        writer.AppendLineIf(member is not null && unmarshals, $"if (!({member?.Member}))");
        using var notMember = member is not null && unmarshals ? writer.Scope() : default;
        switch (shape)
        {
            case { IsCpuConvertedStruct: true, UnmarshalMethodName: { } unmarshal }:
                var native = KernelHostNames.GetLocal(LauncherLocal.Native, shape);
                writer.AppendLine($"{unmarshal}(ref {shape.Identifier}, ref {native}, hStream);");
                break;
            case { IsCpuConvertedArray: true, IsCopiedOut: true, IsReadOnlyContainer: false }:
                ConvertedArrayEmitter.WriteElementUnmarshalLoop(writer, shape, shape.Cpu);
                break;
        }
    }

    // foreach (IntPtr __gsharp_handle in __gsharp_allocated_ptrs_<ord>) GCHandle.FromIntPtr(__gsharp_handle).Free();
    // if (__gsharp_shadow_<ord> != null) ArrayPool<byte>.Shared.Return(__gsharp_shadow_<ord>);                        (converted array)
    // A shared shadow goes back to the pool from its owner only: ... && __gsharp_shared_shadow_<ord> == null
    private static void WriteConvertedRelease(SourceWriter writer, in ArraySharingPlan sharing, int index)
    {
        var shape = sharing.Parameters[index];
        var notShared = sharing.HasShadowSource(index) ? $" && {KernelHostNames.GetLocal(LauncherLocal.SharedShadow, shape)} == null" : "";
        switch (shape)
        {
            case { IsCpuConvertedStruct: true }:
                WriteHandleRelease(writer, shape);
                break;
            case { IsCpuConvertedArray: true }:
                var shadow = KernelHostNames.GetLocal(LauncherLocal.Shadow, shape);
                WriteHandleRelease(writer, shape);
                writer.AppendLine(
                    $"if ({shadow} != null{notShared}) global::System.Buffers.ArrayPool<byte>.Shared.Return({shadow});");
                break;
        }
    }

    // List<IntPtr> __gsharp_allocated_<ord> = new();
    private static void WriteAllocatedPointerList(SourceWriter writer, in KernelParameterShape shape) =>
        writer.AppendLine(
            $"{HostTypeNames.PointerList} {KernelHostNames.GetLocal(LauncherLocal.AllocatedPointers, shape)} = new();");

    // foreach (IntPtr <handle> in __gsharp_allocated_<ord>) GCHandle.FromIntPtr(<handle>).Free();
    private static void WriteHandleRelease(SourceWriter writer, in KernelParameterShape shape)
    {
        var pointers = KernelHostNames.GetLocal(LauncherLocal.AllocatedPointers, shape);
        writer.Append($"foreach ({HostTypeNames.IntPtr} {PinnedHandle} in {pointers}) ");
        writer.AppendLine($"{HostTypeNames.GCHandle}.FromIntPtr({PinnedHandle}).Free();");
    }

    private static void WriteFixedBlocksAndInvoke(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan sharing)
    {
        var hasFixed = false;

        foreach (var shape in kernel.Parameters)
        {
            var pinned = KernelHostNames.GetLocal(LauncherLocal.Pinned, shape);
            // [WHY] fixed (T* p = <array or span>) gives a null pointer for an empty array, which the device would read
            // as null; the pinnable reference is null only when there is no array (ArrayPinning)
            if (shape.IsCpuConvertedArray)
            {
                var shadow = KernelHostNames.GetLocal(LauncherLocal.Shadow, shape);
                writer.AppendLine($"fixed (byte* {pinned} = &{ArrayPinningType}.GetPinnableReference({shadow}))");
                hasFixed = true;
            }
            else if (shape is { IsArray: true, ElementTypeName: { } elementType })
            {
                writer.AppendLine($"fixed ({elementType}* {pinned} = &{shape.PinnableReferenceExpression})");
                hasFixed = true;
            }
            else if (shape is { IsByRef: true, IsCpuConvertedStruct: false })
            {
                writer.AppendLine($"fixed ({shape.TypeName}* {pinned} = &{shape.Identifier})");
                hasFixed = true;
            }
        }

        hasFixed |= AutoResultEmitter.TryWriteCpuFixed(writer, kernel);

        if (hasFixed)
        {
            using (writer.Scope())
            {
                WriteNullableProxies(writer, kernel, sharing);
                WriteDelegateInvocation(writer, kernel, sharing);
            }
        }
        else
        {
            WriteDelegateInvocation(writer, kernel, sharing);
        }
    }

    // Nullable<GSharpArrayProxy> __gsharp_nullable_<ord> = <name>.HasValue ? <proxy> : null;   (Memory<T>? and the
    // like)
    // [WHY] The native launcher takes System_Nullable<GSharpArray<T>> by address; HasValue is the C# value's own, so a
    // default Memory<T> passes as a null array that has a value
    private static void WriteNullableProxies(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan sharing)
    {
        for (var i = 0; i < kernel.Parameters.Length; i++)
        {
            var shape = kernel.Parameters[i];
            if (shape is not { IsArray: true, IsNullableValue: true })
            {
                continue;
            }

            var local = KernelHostNames.GetLocal(LauncherLocal.NullableProxy, shape);
            writer.Append($"global::System.Nullable<{HostTypeNames.ArrayProxy}> {local} = ");
            writer.Append($"{shape.Identifier}.HasValue ? ");
            WriteArrayProxy(writer, shape, AliasGroupEmitter.CpuBufferPointer(sharing.Aliases, i));
            writer.AppendLine(" : null;");
        }
    }

    private static void WriteDelegateInvocation(SourceWriter writer, in KernelHostModel kernel,
        in ArraySharingPlan sharing)
    {
        writer.Append(FunctionHandle);
        using (writer.Parentheses())
        {
            // The native launcher places the dynamic shared memory declarations in sharedMemBytes (#544) and runs the
            // number of blocks that the launch check counted in 64 bits (#805)
            writer.Append("gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, ");
            writer.Append($"sharedMemBytes, {GridBlockCount}");

            for (var i = 0; i < kernel.Parameters.Length; i++)
            {
                writer.Append(", ");
                var group = AliasGroupEmitter.CpuBufferPointer(sharing.Aliases, i);
                WriteDelegateArgument(writer, kernel.Parameters[i], group);
            }

            if (kernel.ReturnsValue)
            {
                writer.Append(", ");
                writer.Append(AutoResultEmitter.PinnedAutoResult);
            }
        }

        writer.AppendLine(";");
        writer.AppendLine($"global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ThrowIfNativeError({DllPath});");
    }

    // [WHY] A switch statement, not a switch expression: each form is written straight into the writer, so no argument
    // string is built (transpiler-emission.md section 1)
    // <group> is the member test and buffer address of a converted member of a group of overlapping arguments, which
    // the native kernel takes instead of the member's own shadow
    private static void WriteDelegateArgument(SourceWriter writer, in KernelParameterShape shape,
        (string Member, string Pointer)? group)
    {
        var pinned = KernelHostNames.GetLocal(LauncherLocal.Pinned, shape);
        switch (shape)
        {
            case { IsArray: true, IsNullableValue: true }:
                writer.Append($"&{KernelHostNames.GetLocal(LauncherLocal.NullableProxy, shape)}");
                break;
            case { IsArray: true }:
                WriteArrayProxy(writer, shape, group);
                break;
            case { IsCpuConvertedStruct: true }:
                writer.AppendIf(group is not null, $"{group?.Member} ? ({shape.AotStructPath}*){group?.Pointer} : ");
                writer.Append($"&{KernelHostNames.GetLocal(LauncherLocal.Native, shape)}");
                break;
            case { IsByRef: true }:
                writer.Append(pinned);
                break;
            case { IsCpuValueStruct: true }:
                // The parameter of Launch is a value parameter, a fixed variable whose address needs no pinning
                writer.Append($"&{shape.Identifier}");
                break;
            default:
                writer.Append(shape.Identifier);
                break;
        }
    }

    // An argument passed as it is:
    // new GSharpArrayProxy { Data = (IntPtr)__gsharp_p_<ord>, Length = <length> }
    // A converted array:
    // new GSharpArrayProxy { Data = <null test> ? IntPtr.Zero : (IntPtr)__gsharp_p_<ord>, Length = <length> }
    // [WHY] The shadow of a null array is an empty rented array, whose pinned address is not null
    // A converted member of a group: ... : <member> ? <buffer address> : (IntPtr)__gsharp_p_<ord> ...
    private static void WriteArrayProxy(SourceWriter writer, in KernelParameterShape shape,
        (string Member, string Pointer)? group)
    {
        var pinned = KernelHostNames.GetLocal(LauncherLocal.Pinned, shape);
        writer.Append($"new {HostTypeNames.ArrayProxy} {{ Data = ");
        writer.AppendIf(shape.IsCpuConvertedArray, $"{shape.NullTestExpression} ? {HostTypeNames.IntPtr}.Zero : ");
        writer.AppendIf(group is not null, $"{group?.Member} ? {group?.Pointer} : ");
        writer.Append($"({HostTypeNames.IntPtr}){pinned}, Length = {shape.LengthExpression} }}");
    }
}
