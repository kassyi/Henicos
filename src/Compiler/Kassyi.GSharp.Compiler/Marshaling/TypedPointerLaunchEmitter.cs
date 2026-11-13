namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the typed <c>Launch{Name}Ptr</c> of an overloaded kernel, which takes <c>GpuBuffer&lt;T&gt;</c> and forwards
/// to the untyped pointer launcher; the forwarding depends on no dialect.
/// </summary>
internal static class TypedPointerLaunchEmitter
{
    // The Launch{Name}Ptr of an overloaded kernel: device pointers are typed GpuBuffer<T>, so C# selects the
    // overload, and the wrapper forwards them to the private implementation: an array as GSharpArrayProxy with the
    // buffer's length, a ref / out parameter as its device pointer.
    /// <summary>Emits the typed pointer launcher of the overloaded <paramref name="kernel"/>.</summary>
    public static void Write(SourceWriter writer, in KernelEmission kernel, Accessibility accessibility)
    {
        writer.AppendGeneratedCodeAttributes();
        writer.Append($"{GeneratedMemberAccessibility.ToKeyword(accessibility)} static unsafe ");
        WrapperEmitter.WriteLaunchReturnType(writer, kernel.Host);
        writer.Append(KernelHostNames.GetTypedPointerLaunchName(kernel.Host));
        using (writer.Parentheses())
        {
            foreach (var p in kernel.Host.Parameters)
            {
                if (p.TryAppendTypedBuffer(writer))
                {
                    writer.Append(TakesNullBuffer(p) ? "? " : " ").Append(p.Identifier);
                    writer.AppendIf(p.HasExplicitDefault, " = null");
                }
                else
                {
                    WrapperEmitter.GetInteropParameterInfo(p, true).WriteTo(writer, p.Identifier);
                }

                writer.Append(", ");
            }

            writer.Append(WrapperEmitter.LaunchOptionParameters);
        }

        writer.AppendLine();
        writer.Append("=> ").Append(KernelHostNames.GetQualifiedPointerLaunchName(kernel));
        using (writer.Parentheses())
        {
            foreach (var p in kernel.Host.Parameters)
            {
                writer.Append(p.Identifier);
                if (p.BufferElementTypeName is not null)
                {
                    // An array carries the buffer's length (#720); a ref / out parameter is the address alone
                    var kind = PointerLaunchPlan.Classify(p);
                    writer.Append((PointerLaunchPlan.IsProxy(kind), TakesNullBuffer(p)) switch
                    {
                        // A GSharpArrayProxy? of a Memory<T>? has no value for a null buffer
                        (true, true) when kind == PointerArgumentKind.NullableArray => "?.ToProxy()",
                        (true, true) => "?.ToProxy() ?? default",
                        (true, false) => ".ToProxy()",
                        (false, true) => "?.DevicePointer ?? global::System.IntPtr.Zero",
                        (false, false) => ".DevicePointer"
                    });
                }

                writer.Append(", ");
            }

            writer.Append(KernelHostNames.LaunchOptionNames);
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    // A buffer parameter that accepts null: one with a default value, or a nullable array (T[]?, Memory<T>?), which a
    // null buffer passes as null
    private static bool TakesNullBuffer(in KernelParameterShape parameter) =>
        parameter.HasExplicitDefault ||
        parameter.IsOptional && PointerLaunchPlan.IsProxy(PointerLaunchPlan.Classify(parameter));
}
