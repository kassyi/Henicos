namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Writes the shadow struct of a <c>Nullable&lt;T&gt;</c> and its marshaling methods from a
/// <see cref="NullableShadowPlan"/>, as the text of one dialect that <see cref="StructMarshalingEmitter"/> places in
/// <c>AotContexts</c>.
/// </summary>
internal static class NullableShadowEmitter
{
    private const string Host = "host";
    private const string Native = "native";
    private const string Value = "value";

    /// <summary>
    /// The shadow struct, <c>Marshal_</c> and <c>Unmarshal_</c>, and the CPU array helpers when planned.
    /// </summary>
    public static string Render(NullableShadowPlan plan, ITranspilerDialect dialect)
    {
        using var writer = new SourceWriter();
        WriteShadow(writer, plan);
        WriteMarshal(writer, plan, dialect);
        WriteUnmarshal(writer, plan, dialect);
        if (plan.ArrayHelpers is { } helpers)
        {
            WriteCopyArrays(writer, plan, helpers);
            WriteHoldsArray(writer, plan, helpers);
        }

        return writer.ToString();
    }

    // [StructLayout(LayoutKind.Explicit, Size = <D>)] public unsafe partial struct <shadow>
    // { [FieldOffset(0)] public byte HasValue; [FieldOffset(<offset>)] public <value field>; }
    private static void WriteShadow(SourceWriter writer, NullableShadowPlan plan)
    {
        writer.AppendLine(
            $"[{HostTypeNames.StructLayout}({HostTypeNames.LayoutKind}.Explicit, Size = {plan.DeviceSize})]");
        writer.AppendLine($"public unsafe partial struct {plan.ShadowName}");
        using (writer.Scope())
        {
            writer.AppendLine($"[{HostTypeNames.FieldOffset}(0)]");
            writer.AppendLine("public byte HasValue;");
            writer.AppendLine($"[{HostTypeNames.FieldOffset}({plan.ValueOffset})]");
            switch (plan.ValueForm)
            {
                case NullableValueForm.Shadow:
                    writer.AppendLine($"public {plan.ValueShadowPath} Value;");
                    break;
                case NullableValueForm.Named:
                    writer.AppendLine($"public {plan.ValueTypeName} Value;");
                    break;
                case NullableValueForm.ArrayView:
                    writer.AppendLine($"public {HostTypeNames.ArrayProxy} Value;");
                    break;
                default:
                    writer.AppendLine($"public fixed byte Value[{plan.ValueSize}];");
                    break;
            }
        }

        writer.AppendLine();
    }

    // public static unsafe void Marshal_<key>(ref <T?> host, ref <shadow> native, <arrays>, IntPtr hStream = default)
    // { native = default; if (host.HasValue) { native.HasValue = 1; <T> value = host.GetValueOrDefault(); <write> } }
    private static void WriteMarshal(SourceWriter writer, NullableShadowPlan plan, ITranspilerDialect dialect)
    {
        writer.Append($"public static unsafe void {plan.MarshalMethodName}");
        using (writer.Parentheses())
        {
            writer.Append($"ref {plan.NullableTypeName} {Host}, ref {plan.ShadowPath} {Native}, ");
            writer.Append($"{plan.ArraysParameterType} {plan.ArraysParameter}, ");
            writer.Append($"{HostTypeNames.IntPtr} hStream = default");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            // [WHY] The native value may be a reused ArrayPool shadow: a null value must read as HasValue false
            writer.AppendLine($"{Native} = default;");
            writer.AppendLine($"if ({Host}.HasValue)");
            using (writer.Scope())
            {
                writer.AppendLine($"{Native}.HasValue = 1;");
                writer.AppendLine($"{plan.ValueTypeName} {Value} = {Host}.GetValueOrDefault();");
                switch (plan.ValueForm)
                {
                    case NullableValueForm.Shadow:
                        writer.Append($"{plan.ValueMarshalMethodName}(ref {Value}, ref {Native}.Value, ");
                        writer.AppendLine($"{plan.ArraysParameter}, hStream);");
                        break;
                    case NullableValueForm.Named:
                        writer.AppendLine($"{Native}.Value = {Value};");
                        break;
                    case NullableValueForm.ArrayView:
                        ArrayViewFieldEmitter.WriteMarshal(
                            writer, GetArrayValue(plan), Value, $"{Native}.Value", dialect);
                        break;
                    default:
                        writer.AppendLine($"fixed (byte* pField = {Native}.Value)");
                        using (writer.Scope())
                        {
                            writer.AppendLine($"*(({plan.ValueTypeName}*)pField) = {Value};");
                        }

                        break;
                }
            }
        }

        writer.AppendLine();
    }

    // public static unsafe void Unmarshal_<key>(ref <T?> host, ref <shadow> native, IntPtr hStream = default)
    // { if (native.HasValue == 0) { host = null; return; }
    //   <T> value = host.GetValueOrDefault(); <read>; host = value; }
    // [WHY] The value starts from the host's, so the arrays that its fields hold stay the same objects and Unmarshal_
    // of the value copies the device's writes back into them
    private static void WriteUnmarshal(SourceWriter writer, NullableShadowPlan plan, ITranspilerDialect dialect)
    {
        writer.Append($"public static unsafe void {plan.UnmarshalMethodName}");
        using (writer.Parentheses())
        {
            writer.Append($"ref {plan.NullableTypeName} {Host}, ref {plan.ShadowPath} {Native}, ");
            writer.Append($"{HostTypeNames.IntPtr} hStream = default");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendLine($"if ({Native}.HasValue == 0)");
            using (writer.Scope())
            {
                writer.AppendLine($"{Host} = null;");
                writer.AppendLine("return;");
            }

            writer.AppendLine($"{plan.ValueTypeName} {Value} = {Host}.GetValueOrDefault();");
            switch (plan.ValueForm)
            {
                case NullableValueForm.Shadow:
                    writer.AppendLine($"{plan.ValueUnmarshalMethodName}(ref {Value}, ref {Native}.Value, hStream);");
                    break;
                case NullableValueForm.Named:
                    writer.AppendLine($"{Value} = {Native}.Value;");
                    break;
                case NullableValueForm.ArrayView:
                    ArrayViewFieldEmitter.WriteUnmarshal(
                        writer, GetArrayValue(plan), Value, $"{Native}.Value", dialect);
                    break;
                default:
                    writer.AppendLine($"fixed (byte* pField = {Native}.Value)");
                    using (writer.Scope())
                    {
                        writer.AppendLine($"{Value} = *(({plan.ValueTypeName}*)pField);");
                    }

                    break;
            }

            writer.AppendLine($"{Host} = {Value};");
        }

        writer.AppendLine();
    }

    // public static void CopyArrays_<key>(ref <T?> host, HostArrayCopies copies)
    // { if (host.HasValue) { <T> value = host.GetValueOrDefault(); CopyArrays_<T>(ref value, copies); host = value; } }
    private static void WriteCopyArrays(SourceWriter writer, NullableShadowPlan plan, NullableArrayHelpers helpers)
    {
        const string Copies = StructArrayHelperEmitter.CopiesParameter;
        writer.Append($"public static void {helpers.CopyArraysMethodName}");
        using (writer.Parentheses())
        {
            writer.Append($"ref {plan.NullableTypeName} {Host}, ");
            writer.Append($"{StructArrayHelperEmitter.HostArrayCopiesType} {Copies}");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendLine($"if ({Host}.HasValue)");
            using (writer.Scope())
            {
                writer.AppendLine($"{plan.ValueTypeName} {Value} = {Host}.GetValueOrDefault();");
                if (plan.ArrayValue is { } view)
                {
                    ArrayViewFieldEmitter.WriteCopyArrays(writer, view, Value);
                }
                else
                {
                    writer.AppendLine($"{helpers.ValueCopyArraysMethodName}(ref {Value}, {Copies});");
                }

                writer.AppendLine($"{Host} = {Value};");
            }
        }

        writer.AppendLine();
    }

    // public static bool HoldsArray_<key>(<T?> host, object array)
    // { return host.HasValue && HoldsArray_<T>(host.GetValueOrDefault(), array); }
    // public static bool HoldsArray_<key>(ReadOnlySpan<<T?>> items, object array)
    // { for (...) { if (HoldsArray_<key>(items[i], array)) { return true; } } return false; }
    private static void WriteHoldsArray(SourceWriter writer, NullableShadowPlan plan, NullableArrayHelpers helpers)
    {
        if (helpers.HoldsArrayMethodName is null)
        {
            return;
        }

        writer.Append($"public static bool {helpers.HoldsArrayMethodName}");
        using (writer.Parentheses())
        {
            writer.Append($"{plan.NullableTypeName} {Host}, object array");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendLine(
                $"return {Host}.HasValue && {helpers.ValueHoldsArrayMethodName}({Host}.GetValueOrDefault(), array);");
        }

        writer.AppendLine();
        writer.Append($"public static bool {helpers.HoldsArrayMethodName}");
        using (writer.Parentheses())
        {
            writer.Append($"{HostTypeNames.ReadOnlySpan}<{plan.NullableTypeName}> items, object array");
        }

        writer.AppendLine();
        using (writer.Scope())
        {
            writer.AppendLine("for (int i = 0; i < items.Length; i++)");
            using (writer.Scope())
            {
                writer.AppendLine($"if ({helpers.HoldsArrayMethodName}(items[i], array))");
                using (writer.Scope())
                {
                    writer.AppendLine("return true;");
                }
            }

            writer.AppendLine("return false;");
        }

        writer.AppendLine();
    }

    private static ArrayFieldView GetArrayValue(NullableShadowPlan plan) =>
        plan.ArrayValue ?? throw new InvalidOperationException($"'{plan.NullableTypeName}' has no array-like value.");
}
