namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the copies of the array arguments that the CPU launcher gives a kernel instead of the caller's array: an
/// <c>[In]</c> array and a read-only container whose elements hold arrays (#798), shared with the holders of the same
/// array as the CUDA launcher shares its device region (#721). The managed fallback and the native call both take them.
/// </summary>
internal static class CpuArrayCopyEmitter
{
    private const string ReferenceEqualsMethod = ArraySharingPlan.ReferenceEqualsMethod;
    private const string ArrayCopies = GSharpInternalNames.Launcher.ArrayCopies;

    // if (<name> != null) {                                          (if (<length> > 0) for a span-like value)
    //   <name> = (<array type>)<name>.Clone();                         (<span>.ToArray() for the other containers)
    // or, for elements that hold arrays:
    //   <E>[] <name>_copy = <the same copy>; for (...) CopyArrays_<E>(ref <name>_copy[<name>_copyIndex]);
    //   <name> = <name>_copy;                                          (ImmutableArray.Create(<name>_copy))
    // }
    // [WHY] The device gets a copy of an [In] array and never sends it back, and never sends back a read-only container
    // either, so on the CPU the kernel works on a copy as well (#798; the GPU result is the one to match). A read-only
    // container is copied only when its elements hold arrays, the one thing a kernel can write through it.
    /// <summary>Emits the copy of every argument that <see cref="ArraySharingPlan.NeedsKernelCopy"/>.</summary>
    public static void WriteKernelCopies(SourceWriter writer, in ArraySharingPlan plan)
    {
        WriteCopyTable(writer, plan);
        AliasGroupEmitter.WriteCpuTypedCopiesFirst(writer, plan);
        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            var shape = plan.Parameters[i];

            // <type> __gsharp_source_<ord> = <name>;          (an [In] T[] that a later [In] T[] may hold as well)
            writer.AppendLineIf(plan.KeepsSource(i),
                $"{shape.TypeName} {KernelHostNames.GetLocal(LauncherLocal.CopySource, shape)} = {shape.Identifier};");

            using (WriteGroupMemberBranch(writer, plan, i))
            {
                if (!plan.NeedsKernelCopy(i))
                {
                    continue;
                }

                // if (<name> != null) for an array, if (<length> > 0) for a span-like value: a null array has nothing
                // to copy, and a copy of an empty span-like value would be one over an array where the argument may
                // have none
                writer.AppendLine(shape.Container == ArrayContainerKind.Array
                    ? $"if ({shape.Identifier} != null)"
                    : $"if ({shape.LengthExpression} > 0)");
                using (writer.Scope())
                {
                    WriteKernelCopyOrShare(writer, plan, i);
                }
            }
        }
    }

    // HostArrayCopies __gsharp_array_copies = new(); __gsharp_array_copies.Keep(<writable T[]>); ...
    // [WHY] The kernel gets a writable array argument itself; an element field of an [In] container that holds the same
    // array must see the kernel's writes to it, as the shared device region shows them on CUDA (#985)
    private static void WriteCopyTable(SourceWriter writer, in ArraySharingPlan plan)
    {
        if (!plan.UsesCopyTable)
        {
            return;
        }

        writer.AppendLine($"{StructArrayHelperEmitter.HostArrayCopiesType} {ArrayCopies} = new();");
        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            var shape = plan.Parameters[i];
            writer.AppendLineIf(shape.IsSharableArray && !plan.NeedsKernelCopy(i),
                $"{ArrayCopies}.Keep({shape.Identifier});");
        }
    }

    // if (__gsharp_aliases?.IsMember(<k>) == true) { <the group's copy, or the caller's memory> }
    // else { <the copy of an [In] argument> }
    // (returns the else scope, or an empty scope when the argument is no member)
    // [WHY] An argument that overlaps another one (a slice, a span over the same array) follows its group
    // (AliasGroupEmitter): the [In] members of a group of [In] arguments share one copy, the members of any other group
    // pass the caller's memory, as the device sees one region for them. A writable member always passes the caller's
    // memory, so it gets no branch
    private static SourceWriterScope WriteGroupMemberBranch(SourceWriter writer, in ArraySharingPlan plan, int index)
    {
        if (!plan.Parameters[index].IsArray || !plan.Aliases.MayTakeGroupCopy(index) ||
            AliasGroupEmitter.MemberTest(plan.Aliases, index) is not { } member)
        {
            return default;
        }

        writer.AppendLine($"if ({member})");
        using (writer.Scope())
        {
            AliasGroupEmitter.WriteCpuMemberCopy(writer, plan, index);
        }

        if (!plan.NeedsKernelCopy(index))
        {
            return default;
        }

        writer.AppendLine("else");
        return writer.Scope();
    }

    // <reuse of an earlier [In] copy> <guard against the holders that share the array with the kernel> { <copy> }
    // [WHY] The CUDA launcher gives every holder of one array one device region, copied back when any holder is
    // writable (#721). So an [In] array that a writable argument or a struct field holds as well is not copied (the
    // kernel writes the caller's array, as the copy back on CUDA does), and [In] arguments that hold one array share one
    // copy.
    private static void WriteKernelCopyOrShare(SourceWriter writer, in ArraySharingPlan plan, int index)
    {
        var shape = plan.Parameters[index];
        if (!shape.IsSharableArray)
        {
            WriteKernelCopy(writer, plan, shape);
            return;
        }

        var hasCondition = TryWriteEarlierCopyReuse(writer, plan, index);
        var guarded = TryWriteSharingHolderGuard(writer, plan, index, hasCondition);
        if (!guarded && !hasCondition)
        {
            WriteKernelCopy(writer, plan, shape);
            return;
        }

        writer.AppendLineIf(!guarded, "else");
        using (writer.Scope())
        {
            WriteKernelCopy(writer, plan, shape);
        }
    }

    // if (ReferenceEquals(<name>, <earlier [In]>_source)) { <name> = <earlier [In]>; } else if (...) ...
    // Returns whether it wrote a condition. An array of another element type that is the same object (int[] / uint[]) is
    // cast through object.
    private static bool TryWriteEarlierCopyReuse(SourceWriter writer, in ArraySharingPlan plan, int index)
    {
        var shape = plan.Parameters[index];
        var hasCondition = false;
        for (var k = 0; k < index; k++)
        {
            if (!plan.IsEarlierCopy(index, k))
            {
                continue;
            }

            var earlier = plan.Parameters[k];
            writer.AppendLine(
                $"{(hasCondition ? "else if" : "if")} ({ReferenceEqualsMethod}({shape.Identifier}, {KernelHostNames.GetLocal(LauncherLocal.CopySource, earlier)}))");
            using (writer.Scope())
            {
                writer.AppendLine(shape.TypeName == earlier.TypeName
                    ? $"{shape.Identifier} = {earlier.Identifier};"
                    : $"{shape.Identifier} = ({shape.TypeName})(object){earlier.Identifier};");
            }

            hasCondition = true;
        }

        return hasCondition;
    }

    // [else ]if (!ReferenceEquals(<name>, <writable T[]>) && !HoldsArray_<S>(<struct or elements>, <name>) && ...)
    // Returns whether it wrote the condition: whether any holder may hand the array to the kernel without a copy
    // (ArraySharingPlan.IsSharingHolder).
    private static bool TryWriteSharingHolderGuard(SourceWriter writer, in ArraySharingPlan plan, int index,
        bool hasCondition)
    {
        var any = false;
        for (var k = 0; k < plan.Parameters.Length; k++)
        {
            any |= plan.IsSharingHolder(index, k);
        }

        if (!any)
        {
            return false;
        }

        var shape = plan.Parameters[index];
        writer.Append(hasCondition ? "else if " : "if ");
        using (writer.Parentheses())
        {
            var first = true;
            for (var k = 0; k < plan.Parameters.Length; k++)
            {
                var holder = plan.Parameters[k];
                if (plan.IsWritableHolder(index, k))
                {
                    writer.AppendIf(!first, " && ");
                    writer.Append($"!{ReferenceEqualsMethod}({shape.Identifier}, {holder.Identifier})");
                    first = false;
                }

                if (plan.IsFieldHolder(index, k) && holder.CpuHoldsArrayMethodName is { } holdsArray)
                {
                    // A struct is passed by value; an array of elements as a span over them (an empty one for null)
                    var value = holder.IsCustomStruct ? holder.Identifier : holder.SpanExpression;
                    writer.AppendIf(!first, " && ");
                    writer.Append($"!{holdsArray}({value}, {shape.Identifier})");
                    first = false;
                }
            }
        }

        writer.AppendLine();
        return true;
    }

    // [WHY] With the launch's HostArrayCopies (ArraySharingPlan.UsesCopyTable), a T[] is copied through the table, so
    // that every holder of one array, an element's field included, gets one copy, and an array that a writable argument
    // holds gets none (#985)
    private static void WriteKernelCopy(SourceWriter writer, in ArraySharingPlan plan, in KernelParameterShape shape)
    {
        var throughTable = plan.UsesCopyTable && shape.Container == ArrayContainerKind.Array;

        // A read-only container (ImmutableArray among them) is copied only for elements that hold arrays
        if (shape.Cpu.ConvertedElementCopyArraysMethodName is { } copyArrays)
        {
            WriteCopyOfElementsWithArrays(writer, shape, copyArrays, throughTable);
        }
        else
        {
            WriteCopyOfElements(writer, shape, throughTable);
        }
    }

    // _ = __gsharp_array_copies.Copy(ref <name>);   or   <name> = <copy of the elements>;
    private static void WriteCopyOfElements(SourceWriter writer, in KernelParameterShape shape, bool throughTable)
    {
        if (throughTable)
        {
            writer.AppendLine($"_ = {ArrayCopies}.Copy(ref {shape.Identifier});");
            return;
        }

        writer.Append($"{shape.Identifier} = ");
        WriteElementsCopy(writer, shape);
        writer.AppendLine(";");
    }

    // <E>[] __gsharp_copy_<ord> = <copy of the elements>;   or   <E>[] ... = <name>; if (copies.Copy(ref ...))
    // { for (...) CopyArrays_<E>(ref __gsharp_copy_<ord>[i], __gsharp_array_copies); }  <name> = <the copy>;
    private static void WriteCopyOfElementsWithArrays(SourceWriter writer, in KernelParameterShape shape,
        string copyArrays, bool throughTable)
    {
        var copy = KernelHostNames.GetLocal(LauncherLocal.CopyBuffer, shape);
        var copyIndex = KernelHostNames.GetLocal(LauncherLocal.CopyIndex, shape);
        writer.Append($"{ConvertedArrayEmitter.GetElementTypeName(shape)}[] {copy} = ");
        if (throughTable)
        {
            writer.AppendLine($"{shape.Identifier};");
            writer.AppendLine($"if ({ArrayCopies}.Copy(ref {copy}))");
        }
        else
        {
            WriteElementsCopy(writer, shape);
            writer.AppendLine(";");
        }

        using (throughTable ? writer.Scope() : default)
        {
            writer.AppendLine($"for (int {copyIndex} = 0; {copyIndex} < {copy}.Length; {copyIndex}++)");
            using (writer.Scope())
            {
                writer.AppendLine($"{copyArrays}(ref {copy}[{copyIndex}], {ArrayCopies});");
            }
        }

        writer.AppendLine(shape.Container == ArrayContainerKind.ImmutableArray
            ? $"{shape.Identifier} = global::System.Collections.Immutable.ImmutableArray.Create({copy});"
            : $"{shape.Identifier} = {copy};");
    }

    // (<array type>)<name>.Clone(), or <span>.ToArray() for the other containers: a new array of the elements
    private static void WriteElementsCopy(SourceWriter writer, in KernelParameterShape shape) =>
        writer.Append(shape.Container == ArrayContainerKind.Array
            ? $"({shape.TypeName}){shape.Identifier}.Clone()"
            : $"{shape.SpanExpression}.ToArray()");
}
