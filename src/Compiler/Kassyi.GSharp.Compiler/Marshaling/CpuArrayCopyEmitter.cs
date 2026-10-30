namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the copies of the array arguments that the CPU launcher gives a kernel instead of the caller's array: an
/// <c>[In]</c> array and a read-only container whose elements hold arrays (#798), shared with the holders of the same
/// array as the CUDA launcher shares its device region (#721). The managed fallback and the native call both take them.
/// </summary>
internal static class CpuArrayCopyEmitter
{
    private const string ReferenceEqualsMethod = ArraySharingPlan.ReferenceEqualsMethod;

    // if (<name> != null) {                                                       (an optional argument only)
    //   <name> = (<array type>)<name>.Clone();                         (<span>.ToArray() for the other containers)
    // or, for elements that hold arrays:
    //   var <name>_copy = <the same copy>; for (...) CopyArrays_<E>(ref <name>_copy[<name>_copyIndex]);
    //   <name> = <name>_copy;                                          (ImmutableArray.Create(<name>_copy))
    // }
    // [WHY] The device gets a copy of an [In] array and never sends it back, and never sends back a read-only container
    // either, so on the CPU the kernel works on a copy as well (#798; the GPU result is the one to match). A read-only
    // container is copied only when its elements hold arrays, the one thing a kernel can write through it.
    /// <summary>Emits the copy of every argument that <see cref="ArraySharingPlan.NeedsKernelCopy"/>.</summary>
    public static void WriteKernelCopies(SourceWriter writer, in ArraySharingPlan plan)
    {
        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            if (!plan.NeedsKernelCopy(i))
            {
                continue;
            }

            var shape = plan.Parameters[i];

            // var <name>_source = <name>;                     (an [In] T[] that a later [In] T[] may hold as well)
            writer.AppendLineIf(plan.KeepsSource(i), $"var {shape.Name}_source = {shape.Identifier};");

            if (shape.IsOptional)
            {
                writer.AppendLine(shape.IsNullableValue ? $"if ({shape.Identifier}.HasValue)" : $"if ({shape.Identifier} != null)");
                using (writer.Scope())
                {
                    WriteKernelCopyOrShare(writer, plan, i);
                }
            }
            else
            {
                WriteKernelCopyOrShare(writer, plan, i);
            }
        }
    }

    // <reuse of an earlier [In] copy> <guard against the holders the kernel gets uncopied> { <copy> }
    // [WHY] The CUDA launcher gives every holder of one array one device region, copied back when any holder is
    // writable (#721). So an [In] array that a writable argument or a struct field holds as well is not copied (the
    // kernel writes the caller's array, as the copy back on CUDA does), and [In] arguments that hold one array share one
    // copy.
    private static void WriteKernelCopyOrShare(SourceWriter writer, in ArraySharingPlan plan, int index)
    {
        var shape = plan.Parameters[index];
        if (!shape.IsSharableArray)
        {
            WriteKernelCopy(writer, shape);
            return;
        }

        var hasCondition = WriteEarlierCopyReuse(writer, plan, index);
        var guarded = WriteUncopiedHolderGuard(writer, plan, index, hasCondition);
        if (!guarded && !hasCondition)
        {
            WriteKernelCopy(writer, shape);
            return;
        }

        writer.AppendLineIf(!guarded, "else");
        using (writer.Scope())
        {
            WriteKernelCopy(writer, shape);
        }
    }

    // if (ReferenceEquals(<name>, <earlier [In]>_source)) { <name> = <earlier [In]>; } else if (...) ...
    // Returns whether it wrote a condition. An array of another element type that is the same object (int[] / uint[]) is
    // cast through object.
    private static bool WriteEarlierCopyReuse(SourceWriter writer, in ArraySharingPlan plan, int index)
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
                $"{(hasCondition ? "else if" : "if")} ({ReferenceEqualsMethod}({shape.Identifier}, {earlier.Name}_source))");
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
    // Returns whether it wrote the condition: whether any holder the kernel gets uncopied may hold the array
    // (ArraySharingPlan.IsUncopiedHolder).
    private static bool WriteUncopiedHolderGuard(SourceWriter writer, in ArraySharingPlan plan, int index,
        bool hasCondition)
    {
        var any = false;
        for (var k = 0; k < plan.Parameters.Length; k++)
        {
            any |= plan.IsUncopiedHolder(index, k);
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

    private static void WriteKernelCopy(SourceWriter writer, in KernelParameterShape shape)
    {
        // A read-only container (ImmutableArray among them) is copied only for elements that hold arrays
        if (shape.Cpu.ConvertedElementCopyArraysMethodName is not { } copyArrays)
        {
            writer.Append($"{shape.Identifier} = ");
            WriteElementsCopy(writer, shape);
            writer.AppendLine(";");
            return;
        }

        var name = shape.Name;
        writer.Append($"var {name}_copy = ");
        WriteElementsCopy(writer, shape);
        writer.AppendLine(";");
        writer.AppendLine($"for (int {name}_copyIndex = 0; {name}_copyIndex < {name}_copy.Length; {name}_copyIndex++)");
        using (writer.Scope())
        {
            writer.AppendLine($"{copyArrays}(ref {name}_copy[{name}_copyIndex]);");
        }

        writer.AppendLine(shape.Container == ArrayContainerKind.ImmutableArray
            ? $"{shape.Identifier} = global::System.Collections.Immutable.ImmutableArray.Create({name}_copy);"
            : $"{shape.Identifier} = {name}_copy;");
    }

    // (<array type>)<name>.Clone(), or <span>.ToArray() for the other containers: a new array of the elements
    private static void WriteElementsCopy(SourceWriter writer, in KernelParameterShape shape) =>
        writer.Append(shape.Container == ArrayContainerKind.Array
            ? $"({shape.TypeName}){shape.Identifier}.Clone()"
            : $"{shape.SpanExpression}.ToArray()");
}
