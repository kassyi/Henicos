
namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits helper methods for tracking, copying, and comparing managed array references within structs.</summary>
internal static class StructArrayHelperEmitter
{
    /// <summary>The runtime type that the CUDA launcher passes to <c>Marshal_</c> as <c>deviceArrays</c>.</summary>
    internal const string DeviceArrayRegionsType = "global::Kassyi.GSharp.Runtime.Interop.DeviceArrayRegions";

    /// <summary>The runtime type that the CPU launcher passes to <c>CopyArrays_</c>: one copy per host array.</summary>
    internal const string HostArrayCopiesType = "global::Kassyi.GSharp.Runtime.Interop.HostArrayCopies";

    // The CopyArrays_ parameter that holds the copies of the launch
    internal const string CopiesParameter = "copies";

    // The Marshal_ parameter that records the arrays of a launch: one device region per host array on CUDA, the
    // GCHandles that pin them on the CPU

    internal static string ArraysParameter(ITranspilerDialect dialect) =>
        dialect.IsCuda ? StructMarshalingEmitter.CudaArraysParameter : StructMarshalingEmitter.CpuArraysParameter;


    // [WHY] The CPU launcher gives a kernel a copy of an [In] array and of a read-only container, whose writes the device
    // never sends back (#798). Copying the elements is not enough when they hold arrays, which the CPU kernel would
    // otherwise write in place, so every array they hold, at any depth, is copied as well. Only a struct that holds
    // arrays gets the method.
    internal static void WriteCopyArraysMethod(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout,
        IAssemblySymbol launcherAssembly) =>
        WriteStructMethod(writer, s, structLayout, launcherAssembly, new CopyArraysAction());

    // _ = copies.Copy(ref <value>);
    // or if (copies.Copy(ref <value>)) { for (...) CopyArrays_<E>(ref <value>[i], copies); }
    // or CopyArrays_<S>(ref <value>, copies); for a struct that holds arrays
    // [WHY] The launch's HostArrayCopies gives every holder of one array the same copy, and none to an array that a
    // writable argument holds (#985); only a new copy has elements still to copy, which also ends the walk of an array
    // that its own elements hold
    internal static void WriteCopyArraysValue(SourceWriter writer, string value, ITypeSymbol type)
    {
        // A view that is not an array is copied as its slice (#1267)
        if (type.IsArrayLikeType() && type is not IArrayTypeSymbol)
        {
            ArrayViewFieldEmitter.WriteCopyArrays(writer, ArrayFieldView.Create(type, HostDialects.Cpu), value);
            return;
        }

        if (!type.TryGetArrayLikeElementType(out var element))
        {
            writer.AppendCopyArraysMethodName(type);
            writer.AppendLine($"(ref {value}, {CopiesParameter});");
            return;
        }

        if (!HoldsArrays(element))
        {
            writer.AppendLine($"_ = {CopiesParameter}.Copy(ref {value});");
            return;
        }

        writer.AppendLine($"if ({CopiesParameter}.Copy(ref {value}))");
        using (writer.Scope())
        {
            writer.AppendLine($"for (int i = 0; i < {value}.Length; i++)");
            using (writer.Scope())
            {
                writer.AppendCopyArraysMethodName(element);
                writer.AppendLine($"(ref {value}[i], {CopiesParameter});");
            }
        }
    }

    /// <summary>The runtime type that the launcher passes to <c>AliasHeldArrays_</c>.</summary>
    internal const string HeldArraysType = "global::Kassyi.GSharp.Runtime.Interop.HostAliasHeldArrays";

    // The AliasHeldArrays_ parameter that collects the arrays
    internal const string HeldParameter = "held";

    // public static void AliasHeldArrays_<key>(ref <S> host, HostAliasHeldArrays held)
    // { <host mirror binding>; if (host.<array> != null) { held.Add(host.<array>, <alignment>, 0, <converted>);
    //   [for (int i = 0; i < host.<array>.Length; i++) AliasHeldArrays_<E>(ref host.<array>[i], held);] }
    //   AliasHeldArrays_<nested>(ref host.<struct>, held); ... }
    // [WHY] A Span<T> argument does not name the array it slices, so a launch that has such an argument beside a struct
    // argument compares the address ranges of the arrays that the struct holds with the ranges of its other arguments
    // (HostAliasHeldArrays), as C# sees one memory in an array field and a slice of the same array (#1849). Only a
    // struct that holds arrays gets the method.
    internal static void WriteAliasHeldArraysMethod(SourceWriter writer, INamedTypeSymbol s,
        StructLayoutInfo structLayout, ITranspilerDialect dialect, IAssemblySymbol launcherAssembly) =>
        WriteStructMethod(writer, s, structLayout, launcherAssembly, new AliasHeldArraysAction(dialect));

    // AliasHeldArrays_<S>(ref <value>, held);   for a struct that holds arrays
    // if (<value> != null) { held.Add(<value>, <alignment>, 0, <converted>);
    //   [for (int i = 0; i < <value>.Length; i++) { AliasHeldArrays_<E>(ref <value>[i], held); }] }   for a T[]
    private static void WriteAliasHeldArraysValue(SourceWriter writer, string value, ITypeSymbol type,
        ITranspilerDialect dialect)
    {
        if (type is not IArrayTypeSymbol arrayType)
        {
            writer.AppendAliasHeldArraysMethodName(type);
            writer.AppendLine($"(ref {value}, {HeldParameter});");
            return;
        }

        var element = arrayType.ElementType;
        var alignment = element.IsReferenceType ? 0 : TypeLayoutCalculator.GetDeviceLayout(element, dialect).Alignment;
        var converted = KernelParameterShape.IsConverted(element, dialect) ? "true" : "false";
        writer.AppendLine($"if ({value} != null)");
        using (writer.Scope())
        {
            writer.AppendLine($"{HeldParameter}.Add({value}, {alignment}, 0, {converted});");
            if (!HoldsComparableArrays(element))
            {
                return;
            }

            writer.AppendLine($"for (int i = 0; i < {value}.Length; i++)");
            using (writer.Scope())
            {
                writer.AppendAliasHeldArraysMethodName(element);
                writer.AppendLine($"(ref {value}[i], {HeldParameter});");
            }
        }
    }

    // An array, or a user struct with an array field at any depth (TransferClassifier.TryFindArrayField), or a Nullable
    // of either
    internal static bool HoldsArrays(ITypeSymbol type) =>
        type.IsArrayLikeType() || type.IsNullableArrayLike() || HostShadowStructs.HoldsArrayFields(type);

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
    internal static void WriteHoldsArrayMethods(SourceWriter writer, INamedTypeSymbol s, StructLayoutInfo structLayout,
        IAssemblySymbol launcherAssembly)
    {
        if (!TransferClassifier.TryFindArrayField(s, out _))
        {
            return;
        }

        WriteStructMethod(writer, s, structLayout, launcherAssembly, new HoldsArrayAction());
        WriteHoldsArrayItemsMethod(writer, s);
    }

    // public static bool HoldsArray_<key>(ReadOnlySpan<<S>> items, object array)
    // { for (int i = 0; i < items.Length; i++) { if (HoldsArray_<key>(items[i], array)) { return true; } } return false; }
    private static void WriteHoldsArrayItemsMethod(SourceWriter writer, INamedTypeSymbol s)
    {
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

    // public static <return type> <name>([ref ]<S> host, <extra parameter>)
    // { <host mirror binding>; <the action on each member that holds arrays>; <the action's last statement> }
    // [WHY] CopyArrays_, AliasHeldArrays_ and HoldsArray_ walk the same members of a struct (the members that hold
    // arrays, an inline array element by element) and differ only in what they do with each value, so one walker
    // takes the action and a new array form is added in one place
    private static void WriteStructMethod<TAction>(SourceWriter writer, INamedTypeSymbol s,
        StructLayoutInfo structLayout, IAssemblySymbol launcherAssembly, TAction action)
        where TAction : struct, IMemberAction
    {
        if (!TransferClassifier.TryFindArrayField(s, out _))
        {
            return;
        }

        var hasMirror = NativeStructEmitter.NeedsHostMirror(s, structLayout, launcherAssembly);
        writer.Append($"public static {action.ReturnType} ");
        action.WriteName(writer, s);
        using (writer.Parentheses())
        {
            writer.Append(action.HostModifier);
            writer.AppendFullyQualifiedCsharpName(s);
            writer.Append(hasMirror ? $" {StructMarshalingEmitter.MirroredHostParameter}" : " host");
            writer.Append($", {action.ExtraParameter}");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            StructBlockCopyEmitter.WriteHostMirrorBinding(writer, s, hasMirror);
            foreach (var member in StructAbiMembers.Get(s))
            {
                WriteMember(writer, member, action);
            }

            action.WriteEnd(writer);
        }

        writer.AppendLine();
    }

    private static void WriteMember<TAction>(SourceWriter writer, StructAbiMember member, TAction action)
        where TAction : struct, IMemberAction
    {
        // GSH0078 reports the member; a fixed buffer holds only primitives
        if (NativeStructEmitter.IsOmittedFromShadow(member) ||
            member.Kind == StructAbiMemberKind.FixedBuffer ||
            !action.Holds(member.Type))
        {
            return;
        }

        if (member.Kind == StructAbiMemberKind.InlineArrayElement)
        {
            // host[k] is the element of the inline array, whatever the field's accessibility
            writer.AppendLine($"for (int k = 0; k < {member.ElementCount}; k++)");
            using (writer.Scope())
            {
                action.WriteValue(writer, "host[k]", member.Type);
            }

            return;
        }

        action.WriteValue(writer, $"host.{member.Name.ToCSharpIdentifier()}", member.Type);
    }

    // What a helper method does with the members of a struct that hold arrays
    private interface IMemberAction
    {
        string ReturnType { get; }

        // "ref " when the method takes the struct by reference, otherwise empty
        string HostModifier { get; }

        // The parameter after the struct
        string ExtraParameter { get; }

        void WriteName(SourceWriter writer, ITypeSymbol type);

        // Whether a member of this type is visited
        bool Holds(ITypeSymbol type);

        void WriteValue(SourceWriter writer, string value, ITypeSymbol type);

        // The statement after the last member
        void WriteEnd(SourceWriter writer);
    }

    private readonly struct CopyArraysAction : IMemberAction
    {
        public string ReturnType => "void";

        public string HostModifier => "ref ";

        public string ExtraParameter => $"{HostArrayCopiesType} {CopiesParameter}";

        public void WriteName(SourceWriter writer, ITypeSymbol type) => writer.AppendCopyArraysMethodName(type);

        public bool Holds(ITypeSymbol type) => HoldsArrays(type);

        public void WriteValue(SourceWriter writer, string value, ITypeSymbol type) =>
            WriteCopyArraysValue(writer, value, type);

        public void WriteEnd(SourceWriter writer)
        {
        }
    }

    private readonly struct AliasHeldArraysAction(ITranspilerDialect dialect) : IMemberAction
    {
        public string ReturnType => "void";

        public string HostModifier => "ref ";

        public string ExtraParameter => $"{HeldArraysType} {HeldParameter}";

        public void WriteName(SourceWriter writer, ITypeSymbol type) => writer.AppendAliasHeldArraysMethodName(type);

        public bool Holds(ITypeSymbol type) => HoldsComparableArrays(type);

        public void WriteValue(SourceWriter writer, string value, ITypeSymbol type) =>
            WriteAliasHeldArraysValue(writer, value, type, dialect);

        public void WriteEnd(SourceWriter writer)
        {
        }
    }

    private readonly struct HoldsArrayAction : IMemberAction
    {
        public string ReturnType => "bool";

        public string HostModifier => "";

        public string ExtraParameter => "object array";

        public void WriteName(SourceWriter writer, ITypeSymbol type) => writer.AppendHoldsArrayMethodName(type);

        public bool Holds(ITypeSymbol type) => HoldsComparableArrays(type);

        public void WriteValue(SourceWriter writer, string value, ITypeSymbol type) =>
            WriteHoldsArrayValue(writer, value, type);

        public void WriteEnd(SourceWriter writer) => writer.AppendLine("return false;");
    }

    // A T[], or a user struct with an array field at any depth (TransferClassifier.TryFindArrayField) or a Nullable of
    // one, whose HoldsArray_ method exists
    internal static bool HoldsComparableArrays(ITypeSymbol type) =>
        type is IArrayTypeSymbol || HostShadowStructs.HoldsArrayFields(type);

    // The host value of a struct with a host mirror arrives under this name, and `host` is then the mirror

    // if (Unsafe.SizeOf<S>() != Unsafe.SizeOf<Mirror>()) LayoutGuard.ThrowSizeMismatch(...);
    // if (!Mirror.FieldOffsetsMatch) LayoutGuard.ThrowFieldOffsetMismatch(...);
    // ref Mirror host = ref Unsafe.As<S, Mirror>(ref hostValue);
    // [WHY] The members are then read and written by the names of the mirror, whose fields name the hidden fields of
    // the captured primary constructor parameters and the fields generated code cannot name
    // (NativeStructEmitter.NeedsHostMirror). The size and offset checks
    // catch a mirror that does not have the CLR layout of S.
}
