
namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits helper methods for tracking, copying, and comparing managed array references within structs.</summary>
internal static class StructArrayHelperEmitter
{
    /// <summary>The runtime type that the CUDA launcher passes to <c>Marshal_</c> as <c>deviceArrays</c>.</summary>
    internal const string DeviceArrayRegionsType = "global::Kassyi.GSharp.Runtime.Interop.DeviceArrayRegions";

    // The Marshal_ parameter that records the arrays of a launch: one device region per host array on CUDA, the
    // GCHandles that pin them on the CPU

    internal static string ArraysParameter(ITranspilerDialect dialect) =>
        dialect.IsCuda ? StructMarshalingEmitter.CudaArraysParameter : StructMarshalingEmitter.CpuArraysParameter;


    // [WHY] The CPU launcher gives a kernel a copy of an [In] array and of a read-only container, whose writes the device
    // never sends back (#798). Copying the elements is not enough when they hold arrays, which the CPU kernel would
    // otherwise write in place, so every array they hold, at any depth, is copied as well. Only a struct that holds
    // arrays gets the method.
    internal static void WriteCopyArraysMethod(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout)
    {
        if (!TransferClassifier.TryFindArrayField(s, out _))
        {
            return;
        }

        var hasMirror = NativeStructEmitter.NeedsHostMirror(s, structLayout);
        writer.Append("public static void ");
        writer.AppendCopyArraysMethodName(s);
        using (writer.Parentheses())
        {
            writer.Append("ref ");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(hasMirror ? $" {StructMarshalingEmitter.MirroredHostParameter}" : " host");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            StructBlockCopyEmitter.WriteHostMirrorBinding(writer, s, hasMirror);
            foreach (var member in StructAbiMembers.Get(s))
            {
                WriteCopyArraysMember(writer, member);
            }
        }

        writer.AppendLine();
    }

    private static void WriteCopyArraysMember(SourceWriter writer, StructAbiMember member)
    {
        // GSH0078 reports the member; a fixed buffer holds only primitives
        if (NativeStructEmitter.IsUnrepresentableReference(member) ||
            member.Kind == StructAbiMemberKind.FixedBuffer ||
            !HoldsArrays(member.Type))
        {
            return;
        }

        if (member.Kind == StructAbiMemberKind.InlineArrayElement)
        {
            // host[k] is the element of the inline array, whatever the field's accessibility
            writer.AppendLine($"for (int k = 0; k < {member.ElementCount}; k++)");
            using (writer.Scope())
            {
                WriteCopyArraysValue(writer, "host[k]", member.Type);
            }

            return;
        }

        WriteCopyArraysValue(writer, $"host.{member.Name.ToCSharpIdentifier()}", member.Type);
    }

    // if (<value> != null) { <value> = (<array type>)<value>.Clone(); [for (...) CopyArrays_<E>(ref <value>[i]);] }
    // or CopyArrays_<S>(ref <value>); for a struct that holds arrays
    private static void WriteCopyArraysValue(SourceWriter writer, string value, ITypeSymbol type)
    {
        if (!type.TryGetArrayLikeElementType(out var element))
        {
            writer.AppendCopyArraysMethodName(type);
            writer.AppendLine($"(ref {value});");
            return;
        }

        writer.AppendLine($"if ({value} != null)");
        using (writer.Scope())
        {
            writer.Append($"{value} = (");
            writer.AppendFullyQualifiedCsharpName(type);
            writer.AppendLine($"){value}.Clone();");
            if (HoldsArrays(element))
            {
                writer.AppendLine($"for (int i = 0; i < {value}.Length; i++)");
                using (writer.Scope())
                {
                    writer.AppendCopyArraysMethodName(element);
                    writer.AppendLine($"(ref {value}[i]);");
                }
            }
        }
    }

    // An array, or a user struct with an array field at any depth (TransferClassifier.TryFindArrayField)
    internal static bool HoldsArrays(ITypeSymbol type) =>
        type.IsArrayLikeType() ||
        type is INamedTypeSymbol named &&
        TypeLayoutCalculator.IsUserStruct(named) &&
        TransferClassifier.TryFindArrayField(named, out _);

    // public static bool HoldsArray_<key>(<S> host, object array)
    // { <host mirror binding>; if (ReferenceEquals(host.<array>, array) [|| HoldsArray_<E>(host.<array>, array)]) return true;
    //   if (HoldsArray_<nested>(host.<struct>, array)) return true; for (int k = 0; k < <N>; k++) { <the same for host[k]> }
    //   return false; }
    // public static bool HoldsArray_<key>(ReadOnlySpan<<S>> items, object array)
    // { for (int i = 0; i < items.Length; i++) { if (HoldsArray_<key>(items[i], array)) { return true; } } return false; }
    // [WHY] On CUDA an [In] array argument and an array field that hold one array share one device region, which the field
    // copies back (#721), so the CPU launcher gives such an [In] argument the caller's array instead of a copy; these
    // methods find the field, at any depth. Only a T[] has an identity to compare, so only T[] fields are compared and
    // walked into. Only a struct that holds arrays gets the methods.
    internal static void WriteHoldsArrayMethods(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout)
    {
        if (!TransferClassifier.TryFindArrayField(s, out _))
        {
            return;
        }

        var hasMirror = NativeStructEmitter.NeedsHostMirror(s, structLayout);
        writer.Append("public static bool ");
        writer.AppendHoldsArrayMethodName(s);
        using (writer.Parentheses())
        {
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(hasMirror ? $" {StructMarshalingEmitter.MirroredHostParameter}, object array" : " host, object array");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            StructBlockCopyEmitter.WriteHostMirrorBinding(writer, s, hasMirror);
            foreach (var member in StructAbiMembers.Get(s))
            {
                WriteHoldsArrayMember(writer, member);
            }

            writer.AppendLine("return false;");
        }

        writer.AppendLine();
        writer.Append("public static bool ");
        writer.AppendHoldsArrayMethodName(s);
        using (writer.Parentheses())
        {
            writer.Append("global::System.ReadOnlySpan<");
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append("> items, object array");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendLine("for (int i = 0; i < items.Length; i++)");
            using (writer.Scope())
            {
                writer.Append("if ");
                using (writer.Parentheses())
                {
                    writer.AppendHoldsArrayMethodName(s);
                    writer.Append("(items[i], array)");
                }

                writer.AppendLine();
                using (writer.Scope())
                {
                    writer.AppendLine("return true;");
                }
            }

            writer.AppendLine("return false;");
        }

        writer.AppendLine();
    }

    private static void WriteHoldsArrayMember(SourceWriter writer, StructAbiMember member)
    {
        // GSH0078 reports the member; a fixed buffer holds only primitives
        if (NativeStructEmitter.IsUnrepresentableReference(member) ||
            member.Kind == StructAbiMemberKind.FixedBuffer ||
            !HoldsComparableArrays(member.Type))
        {
            return;
        }

        if (member.Kind == StructAbiMemberKind.InlineArrayElement)
        {
            // host[k] is the element of the inline array, whatever the field's accessibility
            writer.AppendLine($"for (int k = 0; k < {member.ElementCount}; k++)");
            using (writer.Scope())
            {
                WriteHoldsArrayValue(writer, "host[k]", member.Type);
            }

            return;
        }

        WriteHoldsArrayValue(writer, $"host.{member.Name.ToCSharpIdentifier()}", member.Type);
    }

    // if (ReferenceEquals(<value>, array) [|| HoldsArray_<E>(<value>, array)]) { return true; }    (a T[] value)
    // if (HoldsArray_<S>(<value>, array)) { return true; }                          (a struct that holds T[] fields)
    private static void WriteHoldsArrayValue(SourceWriter writer, string value, ITypeSymbol type)
    {
        writer.Append("if ");
        using (writer.Parentheses())
        {
            if (type is IArrayTypeSymbol arrayType)
            {
                writer.Append($"global::System.Object.ReferenceEquals({value}, array)");
                if (HoldsComparableArrays(arrayType.ElementType))
                {
                    writer.Append(" || ");
                    writer.AppendHoldsArrayMethodName(arrayType.ElementType);
                    writer.Append($"({value}, array)");
                }
            }
            else
            {
                writer.AppendHoldsArrayMethodName(type);
                writer.Append($"({value}, array)");
            }
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendLine("return true;");
        }
    }

    // A T[], or a user struct with an array field at any depth (TransferClassifier.TryFindArrayField), whose HoldsArray_
    // method exists
    internal static bool HoldsComparableArrays(ITypeSymbol type) =>
        type is IArrayTypeSymbol ||
        type is INamedTypeSymbol named &&
        TypeLayoutCalculator.IsUserStruct(named) &&
        TransferClassifier.TryFindArrayField(named, out _);

    // The host value of a struct with a host mirror arrives under this name, and `host` is then the mirror

    // if (Unsafe.SizeOf<S>() != Unsafe.SizeOf<Mirror>()) LayoutGuard.ThrowSizeMismatch(...);
    // if (!Mirror.FieldOffsetsMatch) LayoutGuard.ThrowFieldOffsetMismatch(...);
    // ref var host = ref Unsafe.As<S, Mirror>(ref hostValue);
    // [WHY] The members are then read and written by the names of the mirror, whose fields name the hidden fields of
    // the captured primary constructor parameters (NativeStructEmitter.NeedsHostMirror). The size and offset checks
    // catch a mirror that does not have the CLR layout of S.
}
