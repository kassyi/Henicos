namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Writes the marshaling of a struct field whose array-like type is not an array, from its
/// <see cref="ArrayFieldView"/>: the elements of the view's span (its offset and length), not a whole array object
/// (#1267).
/// </summary>
/// <remarks>
/// A view is a slice, which no array identity names; when it overlaps another argument, its group's region is recorded
/// by pinned address and byte length (<c>DeviceArrayRegions.TryGetViewRegion</c>, #1849), and otherwise its region is
/// the launch's own. A view over no memory (<c>default</c>) reaches the device as a null array, an empty view over an
/// array as an empty one, as for an array field.
/// </remarks>
internal static class ArrayViewFieldEmitter
{
    private const string View = "view";
    private const string Size = "size";
    private const string Element = "element";
    private const string ArrayPinning = HostTypeNames.ArrayPinning;

    /// <summary>Writes the copy of the view <paramref name="host"/> into the proxy <paramref name="native"/>.</summary>
    public static void WriteMarshal(SourceWriter writer, ArrayFieldView view, string host, string native,
        ITranspilerDialect dialect)
    {
        using (writer.Scope())
        {
            if (dialect.IsCuda)
            {
                WriteCudaMarshal(writer, view, host, native);
            }
            else
            {
                WriteCpuMarshal(writer, view, host, native);
            }
        }
    }

    /// <summary>
    /// Writes the copy of the device's writes back into the view <paramref name="host"/>; nothing for a read-only view,
    /// and on the CPU nothing for elements the kernel wrote in place.
    /// </summary>
    public static void WriteUnmarshal(SourceWriter writer, ArrayFieldView view, string host, string native,
        ITranspilerDialect dialect)
    {
        if (view.IsReadOnly || (!dialect.IsCuda && !view.IsConverted))
        {
            return;
        }

        using (writer.Scope())
        {
            writer.AppendLine($"{view.SpanTypeName} {View} = {view.SpanExpression(host)};");
            writer.AppendLine(
                $"if (!{ArrayPinning}.IsNull({View}) && {native}.Data != {HostTypeNames.IntPtr}.Zero)");
            using (writer.Scope())
            {
                if (dialect.IsCuda)
                {
                    WriteCudaCopyBack(writer, view, native);
                }
                else
                {
                    WriteCpuShadowCopyBack(writer, view, native);
                }
            }
        }
    }

    // _ = copies.CopyView(ref <value>); (when elements hold no arrays or views)
    // or if (copies.CopyView(ref <value>)) { <span> view = <span of value>; for (...) <element step>; }
    // or if (copies.CopyView(ref <value>, out <E>[]? view)) { for (...) <element step>; } (read-only view)
    /// <summary>Writes the CPU <c>[In]</c> copy of the view <paramref name="value"/>.</summary>
    public static void WriteCopyArrays(SourceWriter writer, ArrayFieldView view, string value, int depth = 0)
    {
        const string Copies = StructArrayHelperEmitter.CopiesParameter;
        if (view.ElementCopy is not { } elementCopy)
        {
            writer.AppendLine($"_ = {Copies}.CopyView(ref {value});");
            return;
        }

        var viewVar = depth == 0 ? View : $"{View}{depth}";
        var indexVar = StructArrayHelperEmitter.LoopIndex(depth);
        if (!view.IsReadOnly)
        {
            writer.AppendLine($"if ({Copies}.CopyView(ref {value}))");
            using (writer.Scope())
            {
                writer.AppendLine($"{view.SpanTypeName} {viewVar} = {view.SpanExpression(value)};");
                writer.AppendLine($"for (int {indexVar} = 0; {indexVar} < {viewVar}.Length; {indexVar}++)");
                using (writer.Scope())
                {
                    StructArrayHelperEmitter.WriteCopyArraysStep(
                        writer, $"{viewVar}[{indexVar}]", elementCopy, depth + 1);
                }
            }

            return;
        }

        // Wrap in a block so the `out` variable stays scoped to this view and cannot collide with sibling views (CS0136)
        using (writer.Scope())
        {
            writer.AppendLine($"if ({Copies}.CopyView(ref {value}, out {view.ElementTypeName}[]? {viewVar}))");
            using (writer.Scope())
            {
                writer.AppendLine($"for (int {indexVar} = 0; {indexVar} < {viewVar}.Length; {indexVar}++)");
                using (writer.Scope())
                {
                    StructArrayHelperEmitter.WriteCopyArraysStep(
                        writer, $"{viewVar}[{indexVar}]", elementCopy, depth + 1);
                }
            }
        }
    }

    // <span> view = <span of host>; if (!ArrayPinning.IsNull(view)) { int size = ...; if (size > 0)
    // { if (deviceArrays != null && deviceArrays.TryGetViewRegion(view, out IntPtr region)) { <native>.Data = region; }
    //   else { cuMemAllocAsync(out <native>.Data, size, hStream); deviceArrays?.AddOwned(<native>.Data); <copy> } }
    // else { <native>.Data = GSharpArrayProxy.EmptyArrayData; } <native>.Length = view.Length; }
    private static void WriteCudaMarshal(SourceWriter writer, ArrayFieldView view, string host, string native)
    {
        writer.AppendLine($"{view.SpanTypeName} {View} = {view.SpanExpression(host)};");
        writer.AppendLine($"if (!{ArrayPinning}.IsNull({View}))");
        using (writer.Scope())
        {
            WriteSize(writer, view);
            writer.AppendLine($"if ({Size} > 0)");
            using (writer.Scope())
            {
                const string CudaArrays = StructMarshalingEmitter.CudaArraysParameter;
                writer.AppendLine(
                    $"if ({CudaArrays} != null && {CudaArrays}.TryGetViewRegion({View}, out {HostTypeNames.IntPtr} region))");
                using (writer.Scope())
                {
                    writer.AppendLine($"{native}.Data = region;");
                }

                writer.AppendLine("else");
                using (writer.Scope())
                {
                    writer.AppendLine(
                        $"{HostTypeNames.CudaException}.ThrowIfError({HostTypeNames.CudaDriverApi}.cuMemAllocAsync(" +
                        $"out {native}.Data, (global::System.IntPtr){Size}, hStream));");
                    writer.AppendLine($"{CudaArrays}?.AddOwned({native}.Data);");
                    if (view.IsConverted)
                    {
                        WriteShadowMarshalLoop(writer, view, "tempArray", CudaArrays);
                        StructBlockCopyEmitter.WritePinnedCopy(writer, "tempArray", $"{native}.Data", toDevice: true);
                    }
                    else
                    {
                        WritePinnedSpanCopy(writer, view, native, toDevice: true);
                    }
                }
            }

            writer.AppendLine("else");
            using (writer.Scope())
            {
                writer.AppendLine($"{native}.Data = {HostTypeNames.ArrayProxy}.EmptyArrayData;");
            }

            writer.AppendLine($"{native}.Length = {View}.Length;");
        }
    }

    // int size = ...; if (size > 0) { <copy back the span, or the shadow array and Unmarshal_ each element> }
    private static void WriteCudaCopyBack(SourceWriter writer, ArrayFieldView view, string native)
    {
        WriteSize(writer, view);
        writer.AppendLine($"if ({Size} > 0)");
        using (writer.Scope())
        {
            if (!view.IsConverted)
            {
                WritePinnedSpanCopy(writer, view, native, toDevice: false);
                return;
            }

            writer.AppendLine($"{view.ElementShadowPath}[] tempArray = new {view.ElementShadowPath}[{View}.Length];");
            StructBlockCopyEmitter.WritePinnedCopy(writer, "tempArray", $"{native}.Data", toDevice: false);
            writer.AppendLine($"for (int i = 0; i < {View}.Length; i++)");
            using (writer.Scope())
            {
                writer.AppendLine($"{view.ElementUnmarshalMethodName}(ref {View}[i], ref tempArray[i], hStream);");
            }
        }
    }

    // List<IntPtr> pinnedHandles = allocatedPtrs ?? throw ...;
    // <native>.Data = HostArrayViews.Pin<E>(<memory of host>, pinnedHandles); <native>.Length = <memory>.Length;
    // or, for converted elements, a pinned shadow array of the span's elements
    private static void WriteCpuMarshal(SourceWriter writer, ArrayFieldView view, string host, string native)
    {
        const string Handles = StructMarshalingEmitter.CpuArraysParameter;
        writer.Append($"{HostTypeNames.PointerList} pinnedHandles = {Handles} ?? ");
        writer.AppendLine($"throw new {HostTypeNames.ArgumentNullException}(\"{Handles}\");");
        if (!view.IsConverted)
        {
            var memory = view.MemoryExpression(host);
            writer.AppendLine(
                $"{native}.Data = global::Kassyi.GSharp.Runtime.Interop.HostArrayViews.Pin<{view.ElementTypeName}>" +
                $"({memory}, pinnedHandles);");
            writer.AppendLine($"{native}.Length = {memory}.Length;");
            return;
        }

        writer.AppendLine($"{view.SpanTypeName} {View} = {view.SpanExpression(host)};");
        writer.AppendLine($"if (!{ArrayPinning}.IsNull({View}))");
        using (writer.Scope())
        {
            WriteShadowMarshalLoop(writer, view, "shadow", Handles);
            const string GcHandle = HostTypeNames.GCHandle;
            writer.AppendLine($"{GcHandle} handle = {GcHandle}.Alloc(shadow, {HostTypeNames.GCHandleType}.Pinned);");
            writer.AppendLine($"pinnedHandles.Add({GcHandle}.ToIntPtr(handle));");
            writer.AppendLine($"{native}.Data = handle.AddrOfPinnedObject();");
            writer.AppendLine($"{native}.Length = {View}.Length;");
        }
    }

    // <shadow>* shadow = (<shadow>*)<native>.Data; for (...) Unmarshal_<E>(ref view[i], ref shadow[i], hStream);
    private static void WriteCpuShadowCopyBack(SourceWriter writer, ArrayFieldView view, string native)
    {
        writer.AppendLine($"{view.ElementShadowPath}* shadow = ({view.ElementShadowPath}*){native}.Data;");
        writer.AppendLine($"for (int i = 0; i < {View}.Length; i++)");
        using (writer.Scope())
        {
            writer.AppendLine($"{view.ElementUnmarshalMethodName}(ref {View}[i], ref shadow[i], hStream);");
        }
    }

    // <shadow>[] <local> = new <shadow>[view.Length];
    // for (...) { Marshal_<E>(ref view[i], ref <local>[i], <arrays>, hStream); }
    // (a read-only view passes a copy of view[i])
    private static void WriteShadowMarshalLoop(SourceWriter writer, ArrayFieldView view, string local, string arrays)
    {
        writer.AppendLine($"{view.ElementShadowPath}[] {local} = new {view.ElementShadowPath}[{View}.Length];");
        writer.AppendLine($"for (int i = 0; i < {View}.Length; i++)");
        using (writer.Scope())
        {
            var element = $"{View}[i]";
            if (view.IsReadOnly)
            {
                // A read-only span gives no ref to its element; Marshal_ only reads its copy
                writer.AppendLine($"{view.ElementTypeName} {Element} = {View}[i];");
                element = Element;
            }

            writer.AppendLine($"{view.ElementMarshalMethodName}(ref {element}, ref {local}[i], {arrays}, hStream);");
        }
    }

    // int size = checked(view.Length * sizeof(<element or its shadow>));
    private static void WriteSize(SourceWriter writer, ArrayFieldView view) =>
        writer.AppendLine(
            $"int {Size} = checked({View}.Length * sizeof({view.ElementShadowPath ?? view.ElementTypeName}));");

    // fixed (<E>* pView = view) { cuMemcpy<HtoD | DtoH>Async_v2(...); }
    private static void WritePinnedSpanCopy(SourceWriter writer, ArrayFieldView view, string native, bool toDevice)
    {
        writer.AppendLine($"fixed ({view.ElementTypeName}* pView = {View})");
        using (writer.Scope())
        {
            writer.AppendLine(toDevice
                ? $"{HostTypeNames.CudaException}.ThrowIfError({HostTypeNames.CudaDriverApi}.cuMemcpyHtoDAsync_v2(" +
                $"{native}.Data, (global::System.IntPtr)pView, (global::System.IntPtr){Size}, hStream));"
                : $"{HostTypeNames.CudaException}.ThrowIfError({HostTypeNames.CudaDriverApi}.cuMemcpyDtoHAsync_v2(" +
                $"(global::System.IntPtr)pView, {native}.Data, (global::System.IntPtr){Size}, hStream));");
        }
    }
}
