namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits dynamic two-form kernel selection and parameter marshaling for CUDA kernels with by-address in parameters.
/// </summary>
internal static class CudaTwoFormsLaunchEmitter
{
    /// <summary>
    /// Whether the kernel requires two-form emission and dynamic selection (has at least one in parameter
    /// passed by address).
    /// </summary>
    public static bool HasTwoForms(in KernelHostModel kernel) =>
        kernel.Parameters.AsStackEnumerable().Any(static p => p.IsInReference);

    /// <summary>
    /// Emits the CudaKernelFunction fields for both the by-value and by-address forms.
    /// </summary>
    public static void WriteFunctionFields(SourceWriter writer, in KernelEmission kernel, string moduleName)
    {
        const string FunctionType = "global::Kassyi.GSharp.Runtime.Aot.CudaKernelFunction";
        var field = KernelHostNames.GetFunctionPointerFieldName(kernel.MemberKey);
        var moduleContextName = AotModuleNames.GetModuleContextName(moduleName);

        writer.Append($"private static readonly {FunctionType} {field} = new {FunctionType}");
        using (writer.Parentheses())
        {
            writer.Append($"\"{kernel.Host.KernelName}\", ");
            writer.Append($"global::Kassyi.GSharp.Generated.{moduleContextName}.GetFatbin");
        }
        writer.AppendLine(";");

        if (HasTwoForms(kernel.Host))
        {
            var byAddressField = field + "__by_address";
            var byAddressKernelName = kernel.Host.KernelName + GSharpInternalNames.Kernel.InByAddressSuffix;
            writer.Append($"private static readonly {FunctionType} {byAddressField} = new {FunctionType}");
            using (writer.Parentheses())
            {
                writer.Append($"\"{byAddressKernelName}\", ");
                writer.Append($"global::Kassyi.GSharp.Generated.{moduleContextName}.GetFatbin");
            }
            writer.AppendLine(";");
        }
    }

    /// <summary>
    /// Emits runtime alias checking for in parameters and dynamically selects the by-address function handle
    /// if aliased.
    /// </summary>
    public static void WriteDynamicSelection(SourceWriter writer, in KernelEmission kernel, in ArraySharingPlan plan)
    {
        var parameters = kernel.Host.Parameters;
        writer.Append("bool __gsharp_in_aliased = ");

        var first = true;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!parameters[i].IsInReference)
            {
                continue;
            }

            if (AliasGroupEmitter.MemberTest(plan.Aliases, i) is { } test)
            {
                if (!first)
                {
                    writer.Append(" || ");
                }
                writer.Append(test);
                first = false;
            }
        }

        if (first)
        {
            writer.Append("false");
        }

        writer.AppendLine(";");
        writer.AppendLine("if (__gsharp_in_aliased)");
        using (writer.Scope())
        {
            var className = kernel.Host.ClassQualifiedName;
            var byAddressField = KernelHostNames.GetFunctionPointerFieldName(kernel.MemberKey) + "__by_address";
            writer.Append($"{GSharpInternalNames.Launcher.FunctionHandle} = {className}.{byAddressField}.Resolve");
            using (writer.Parentheses())
            {
                if (!kernel.ErrorSites.IsDefaultOrEmpty)
                {
                    writer.Append($"{className}.{KernelHostNames.ErrorSitesFieldName}");
                }
            }
            writer.AppendLine(";");
        }
    }

    /// <summary>
    /// Emits dynamic marshaling for a scalar in parameter: by-value stack pointer if non-aliased,
    /// or device pointer if aliased.
    /// </summary>
    public static void WriteInScalarMarshal(SourceWriter writer, in ArraySharingPlan plan, int paramIndex)
    {
        var shape = plan.Parameters[paramIndex];
        writer.AppendLine("if (__gsharp_in_aliased)");
        using (writer.Scope())
        {
            var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
            var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
            var pinnedTemp = KernelHostNames.GetLocal(LauncherLocal.PinnedTemp, shape);
            writer.AppendLine($"// Marshal ref/out scalar: {shape.Identifier}");
            writer.AppendLine($"int {size} = sizeof({shape.TypeName});");
            using (KernelLaunchEmitter.WriteGroupMemberBranch(writer, plan, paramIndex))
            {
                KernelLaunchEmitter.WriteDeviceAllocation(writer, device, size);
                if (shape.RefKind != RefKind.Out)
                {
                    writer.AppendLine("// Fast zero-allocation copy to device");
                    writer.AppendLine($"fixed ({shape.TypeName}* {pinnedTemp} = &{shape.Identifier})");
                    using (writer.Scope())
                    {
                        var source = $"({HostTypeNames.IntPtr}){pinnedTemp}";
                        KernelLaunchEmitter.WriteSynchronizedCopy(
                            writer, KernelLaunchEmitter.CopyDirection.HostToDevice, device, source, size);
                    }
                }
            }
            writer.AppendLine($"{KernelLaunchEmitter.KernelParams}[{paramIndex}] = &{device};");
        }
        writer.AppendLine("else");
        using (writer.Scope())
        {
            writer.AppendLine($"// Marshal scalar: {shape.Identifier}");
            var valLocal = $"__gsharp_val_{paramIndex}";
            writer.AppendLine($"{shape.TypeName} {valLocal} = {shape.Identifier};");
            writer.AppendLine($"{KernelLaunchEmitter.KernelParams}[{paramIndex}] = &{valLocal};");
        }
    }

    /// <summary>
    /// Emits dynamic marshaling for a custom struct in parameter: local native struct pointer if non-aliased,
    /// or device pointer if aliased.
    /// </summary>
    public static void WriteInCustomStructMarshal(
        SourceWriter writer, in ArraySharingPlan plan, int paramIndex, string native)
    {
        var shape = plan.Parameters[paramIndex];
        writer.AppendLine("if (__gsharp_in_aliased)");
        using (writer.Scope())
        {
            var size = KernelHostNames.GetLocal(LauncherLocal.Size, shape);
            var device = KernelHostNames.GetLocal(LauncherLocal.DevicePointer, shape);
            writer.AppendLine($"// Marshal ref/out struct: {shape.Identifier}");
            writer.AppendLine($"int {size} = sizeof({shape.AotStructPath});");
            using (KernelLaunchEmitter.WriteGroupMemberBranch(writer, plan, paramIndex))
            {
                KernelLaunchEmitter.WriteDeviceAllocation(writer, device, size);
                if (shape.RefKind != RefKind.Out)
                {
                    writer.AppendLine("// Fast zero-allocation copy to device");
                    var source = $"({HostTypeNames.IntPtr})(&{native})";
                    KernelLaunchEmitter.WriteSynchronizedCopy(
                        writer, KernelLaunchEmitter.CopyDirection.HostToDevice, device, source, size);
                }
            }
            writer.AppendLine($"{KernelLaunchEmitter.KernelParams}[{paramIndex}] = &{device};");
        }
        writer.AppendLine("else");
        using (writer.Scope())
        {
            writer.AppendLine($"// Marshal scalar struct: {shape.Identifier}");
            writer.AppendLine($"{KernelLaunchEmitter.KernelParams}[{paramIndex}] = &{native};");
        }
    }
}
