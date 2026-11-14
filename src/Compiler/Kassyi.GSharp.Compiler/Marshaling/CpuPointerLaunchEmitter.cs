namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the CPU <c>Launch{Kernel}Ptr</c> launcher, which has the signature of the CUDA one
/// (<see cref="WrapperEmitter.WriteLaunchMethodSignature"/>) and calls the native launcher export through an unmanaged
/// function pointer, from <see cref="CpuPointerLaunchPlan"/>.
/// </summary>
internal static class CpuPointerLaunchEmitter
{
    private const string FunctionHandle = GSharpInternalNames.Launcher.FunctionHandle;
    private const string DllPath = GSharpInternalNames.Launcher.DllPath;
    private const string CpuKernelLoader = HostTypeNames.CpuKernelLoader;

    // <checks of the proxies>; <launch check>; <auto result>; <C# execution: CannotRun>; <dll path>;
    // if (ShouldUseManagedExecution(<dll path>)) { throw new NotSupportedException(<message>); }
    // <function pointer> __gsharp_hfunc = (<function pointer>)CpuKernelLoader.LoadAotKernelPtr(<export>, <dll path>);
    // <error sites>; [fixed (<result>* p = <auto result>)] { <nullable proxies>; __gsharp_hfunc(...); <error check> }
    /// <summary>Emits the CPU pointer launcher of <paramref name="kernel"/>.</summary>
    public static void Write(SourceWriter writer, in KernelEmission kernel, Accessibility accessibility,
        string moduleName)
    {
        var plan = CpuPointerLaunchPlan.Create(kernel.Host);
        using var suppression = writer.WarningSuppressed(ManagedPointerWarning.Id);
        WrapperEmitter.WriteLaunchMethodSignature(writer, kernel, isDevicePtr: true, accessibility);
        using (writer.Scope())
        {
            WrapperEmitter.WritePointerArrayArgumentChecks(writer, kernel.Host);
            LaunchLimitEmitter.WriteCpuCheck(writer, kernel.Host);
            AutoResultEmitter.WriteAllocation(writer, kernel.Host);
            CSharpExecutionEmitter.WriteBranch(writer, CSharpExecutionPlan.Create(kernel.Host, isDevicePtr: true));
            CpuHostLaunchEmitter.WriteDllPath(writer, moduleName);
            WriteManagedExecutionBranch(writer, plan.ManagedExecutionMessage);
            WriteFunctionPointer(writer, kernel.Host, plan.Arguments);
            CpuHostLaunchEmitter.WriteErrorSiteRegistration(writer, kernel);
            // [WHY] No alias groups (LaunchAliasPlan): the native kernel reads and writes the caller's memory through
            // the caller's addresses, with no copy or shadow of its own, so overlapping arguments already overlap there
            WriteResultAndCall(writer, kernel.Host, plan.Arguments);
        }
    }

    // if (CpuKernelLoader.ShouldUseManagedExecution(__gsharp_dll_path)) { throw new NotSupportedException(<message>); }
    private static void WriteManagedExecutionBranch(SourceWriter writer, string message)
    {
        writer.AppendLine($"if ({CpuKernelLoader}.ShouldUseManagedExecution({DllPath}))");
        using (writer.Scope())
        {
            var literal = SymbolDisplay.FormatLiteral(message, quote: true);
            writer.AppendLine($"throw new {HostTypeNames.NotSupportedException}({literal});");
        }
    }

    // <function pointer> __gsharp_hfunc = (<function pointer>)CpuKernelLoader.LoadAotKernelPtr("<export>", <dll path>);
    // [WHY] The type is written out rather than var: a user type named var would take the declaration
    private static void WriteFunctionPointer(SourceWriter writer, in KernelHostModel kernel,
        in CpuNativeArgumentPlan arguments)
    {
        WriteFunctionPointerType(writer, arguments);
        writer.Append($" {FunctionHandle} = ");
        using (writer.Parentheses())
        {
            WriteFunctionPointerType(writer, arguments);
        }

        writer.Append($"{CpuKernelLoader}.LoadAotKernelPtr");
        writer.AppendLine($"(\"{kernel.LaunchExportName}\", {DllPath});");
    }

    // delegate* unmanaged[Cdecl]<<launch dimensions>, <arguments>[, <result>], void>
    private static void WriteFunctionPointerType(SourceWriter writer, in CpuNativeArgumentPlan arguments)
    {
        writer.Append("delegate* unmanaged[Cdecl]<uint, uint, uint, uint, uint, uint, uint, long, ");
        foreach (var argument in arguments.Arguments)
        {
            writer.Append($"{argument.TypeName}, ");
        }

        writer.AppendIf(arguments.ResultTypeName is not null, $"{arguments.ResultTypeName}, ");
        writer.Append("void>");
    }

    // A converted returned struct: <declarations>; try { <shadow>; <call>; <conversion> } finally { <release> }
    private static void WriteResultAndCall(SourceWriter writer, in KernelHostModel kernel,
        in CpuNativeArgumentPlan arguments)
    {
        if (!AutoResultEmitter.TryWriteCpuConvertedDeclaration(writer, kernel))
        {
            WritePinnedCall(writer, kernel, arguments);
            AutoResultEmitter.WriteReturn(writer, kernel);
            return;
        }

        writer.AppendLine("try");
        using (writer.Scope())
        {
            AutoResultEmitter.WriteCpuConvertedMarshal(writer, kernel);
            WritePinnedCall(writer, kernel, arguments);
            AutoResultEmitter.WriteCpuConvertedUnmarshal(writer, kernel);
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            AutoResultEmitter.WriteCpuConvertedRelease(writer, kernel);
        }

        AutoResultEmitter.WriteReturn(writer, kernel);
    }

    // [fixed (<result>* p___gsharp_auto_result = <auto result or its shadow>)] { <nullable proxies>; <call> }
    // [WHY] Only the auto result is a managed array; every other argument is the caller's address or value
    private static void WritePinnedCall(SourceWriter writer, in KernelHostModel kernel,
        in CpuNativeArgumentPlan arguments)
    {
        if (!AutoResultEmitter.TryWriteCpuFixed(writer, kernel))
        {
            WriteNullableProxies(writer, kernel, arguments);
            CpuNativeCallEmitter.WriteCall(writer, kernel, arguments);
            return;
        }

        using (writer.Scope())
        {
            WriteNullableProxies(writer, kernel, arguments);
            CpuNativeCallEmitter.WriteCall(writer, kernel, arguments);
        }
    }

    // Nullable<GSharpArrayProxy> __gsharp_nullable_<ord> = <name>.HasValue ? <copy of <name>.Value> : null;
    // [WHY] The native launcher takes System_Nullable<GSharpArray<T>> by address, so the caller's value is copied into
    // a local whose address the call passes
    private static void WriteNullableProxies(SourceWriter writer, in KernelHostModel kernel,
        in CpuNativeArgumentPlan arguments)
    {
        var parameters = kernel.Parameters;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (arguments.Arguments[i].Form != CpuNativeArgumentForm.NullableProxyAddress)
            {
                continue;
            }

            var shape = parameters[i];
            var local = KernelHostNames.GetLocal(LauncherLocal.NullableProxy, shape);
            writer.Append($"global::System.Nullable<{HostTypeNames.ArrayProxy}> {local} = ");
            writer.Append($"{shape.Identifier}.HasValue ? ");
            CpuNativeCallEmitter.WriteProxyCopy(writer, shape.Identifier + ".Value");
            writer.AppendLine(" : null;");
        }
    }
}
