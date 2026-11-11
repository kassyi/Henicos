namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Emits launcher bodies that load a kernel from a fatbin module next to the application and launch it
/// through the CUDA driver API. Shared by <c>[CudaImport]</c> and YAML <c>moduleName</c> bindings.
/// </summary>
/// <remarks>
/// Writes a <see cref="ModuleKernelLaunchPlan"/> and decides nothing: the planners (<see cref="CudaImportPlanner"/>,
/// <see cref="ModuleBindingPlanner"/>) choose which parameters are kernel arguments, how each is passed and which set
/// the launch. Managed arrays are staged through <c>GpuBuffer&lt;T&gt;</c>. When a stream is given, synchronization is
/// scoped to that stream.
/// </remarks>
internal static class ModuleKernelLaunchEmitter
{
    // The launcher-local struct that packs the kernel arguments for cuLaunchKernel; followed by the method name.
    private const string NativeContextPrefix = "__gsharp_native_context_";

    private const string AddressOf = "global::Kassyi.GSharp.Runtime.Aot.ModuleKernelArguments.AddressOf";

    private const string Zero = HostTypeNames.IntPtr + ".Zero";

    private const string ThrowIfError = HostTypeNames.CudaException + ".ThrowIfError";

    public static void EmitModuleCacheField(SourceWriter writer, string moduleName)
    {
        writer.Append("private static readonly Lazy<byte[]> s_fatbin_");
        writer.Append(AotModuleNames.ToIdentifierPart(moduleName));
        writer.Append(" = new(() => System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, ");
        AppendStringLiteral(writer, moduleName);
        writer.AppendLine(")));");
        writer.AppendLine();
    }

    public static void EmitNativeContextStruct(SourceWriter writer, string methodName, ModuleKernelLaunchPlan plan)
    {
        writer.AppendLine($"[{HostTypeNames.StructLayout}({HostTypeNames.LayoutKind}.Sequential)]");
        writer.AppendLine($"private struct {NativeContextPrefix}{methodName}");
        using (writer.Scope())
        {
            foreach (var argument in plan.Arguments)
            {
                writer.AppendLine(argument.IsArray
                    ? $"public {HostTypeNames.IntPtr} {argument.Name};"
                    : $"public {argument.TypeName} {argument.Name};");
            }
        }

        writer.AppendLine();
    }

    /// <summary>
    /// Emits the statements of a launcher body (without the enclosing braces).
    /// </summary>
    /// <param name="writer">The destination source writer.</param>
    /// <param name="moduleName">The native module name used to look up the embedded fatbin field.</param>
    /// <param name="entryPoint">The mangled native kernel entry-point symbol.</param>
    /// <param name="methodName">The C# launcher method name.</param>
    /// <param name="launcherName">The launcher as <c>Type.Method</c>, named by the shared memory limit error.</param>
    /// <param name="plan">The kernel arguments and launch settings; it must have no problems.</param>
    /// <param name="packContext">Whether struct parameters are packed into a generated launch context struct.</param>
    public static void EmitLaunchBody(
        SourceWriter writer,
        string moduleName,
        string entryPoint,
        string methodName,
        string launcherName,
        ModuleKernelLaunchPlan plan,
        bool packContext)
    {
        if (!plan.CanLaunch)
        {
            throw new InvalidOperationException($"The launcher of '{launcherName}' has parameters it cannot pass.");
        }

        var launch = plan.Launch;
        WriteLaunchLimitCheck(writer, launcherName, launch);
        writer.AppendLine($"using var scope = new {HostTypeNames.GpuKernelLoader}.CudaContextScope();");
        writer.Append($"var kernel = {HostTypeNames.GpuKernelLoader}.LoadAotKernel(s_fatbin_");
        writer.Append(AotModuleNames.ToIdentifierPart(moduleName));
        writer.Append(".Value, ");
        AppendStringLiteral(writer, entryPoint);
        writer.AppendLine(");");

        // [WHY] sharedMemBytes above 48 KB needs the function's opt-in, and a request the device cannot hold is
        // rejected with the launcher's name instead of the driver's bare CUDA_ERROR_INVALID_VALUE, as in
        // CudaHostLaunchEmitter (#545, #820). Without the parameter the launch requests no dynamic shared memory.
        if (launch.SharedMemBytes is { } sharedMemBytes)
        {
            writer.Append(
                $"global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare(kernel, {sharedMemBytes}, ");
            AppendStringLiteral(writer, launcherName);
            writer.AppendLine(");");
        }

        writer.AppendLine();

        EmitBufferAllocations(writer, plan.Arguments);
        if (packContext)
        {
            EmitContextInitialization(writer, methodName, plan.Arguments);
        }

        EmitKernelLaunch(writer, methodName, plan, packContext);
        EmitBufferOutputs(writer, plan.Arguments);
    }

    /// <summary>
    /// Emits a launcher body that throws <see cref="NotSupportedException"/> with <paramref name="message"/>, for a
    /// method whose parameters the launcher cannot pass (the analyzer reports the same message as an error).
    /// </summary>
    // throw new global::System.NotSupportedException("<message>");
    public static void EmitUnavailableBody(SourceWriter writer, string message) =>
        writer.AppendLine(
            $"throw new global::System.NotSupportedException({SymbolDisplay.FormatLiteral(message, quote: true)});");

    // global::Kassyi.GSharp.Runtime.LaunchLimits.CheckLaunch(<gridDimX or 1>, ..., <blockDimZ or 1>, "<launcher>");
    // [WHY] A launch beyond the limits of every GPU is rejected with the same ArgumentOutOfRangeException as the
    // launchers of [GpuKernel] methods (LaunchLimitEmitter, #799), before the context and the device allocations. A
    // launcher without dimension parameters launches 1 x 1 x 1 blocks of 1 thread, which needs no check.
    private static void WriteLaunchLimitCheck(SourceWriter writer, string launcherName,
        ModuleLaunchConfiguration launch)
    {
        if (!launch.HasDimensions)
        {
            return;
        }

        writer.Append("global::Kassyi.GSharp.Runtime.LaunchLimits.CheckLaunch");
        using (writer.Parentheses())
        {
            writer.Append($"{launch.GridDimX ?? "1"}, {launch.GridDimY ?? "1"}, {launch.GridDimZ ?? "1"}, ");
            writer.Append($"{launch.BlockDimX ?? "1"}, {launch.BlockDimY ?? "1"}, {launch.BlockDimZ ?? "1"}, ");
            AppendStringLiteral(writer, launcherName);
        }

        writer.AppendLine(";");
    }

    /// <summary>
    /// Appends <paramref name="value"/> as an escaped C# regular string literal, including the quotes.
    /// </summary>
    public static void AppendStringLiteral(SourceWriter writer, string value)
    {
        writer.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    writer.Append("\\\"");
                    break;
                case '\\':
                    writer.Append("\\\\");
                    break;
                case '\n':
                    writer.Append("\\n");
                    break;
                case '\r':
                    writer.Append("\\r");
                    break;
                case '\t':
                    writer.Append("\\t");
                    break;
                case '\0':
                    writer.Append("\\0");
                    break;
                default:
                    writer.Append(c);
                    break;
            }
        }

        writer.Append('"');
    }

    private static void EmitBufferAllocations(SourceWriter writer, EquatableArray<ModuleKernelArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            if (!argument.IsArray)
            {
                continue;
            }

            writer.Append($"using var d_{argument.Name} = new {HostTypeNames.GpuBuffer}<{argument.TypeName}>");
            writer.AppendLine($"({argument.Name}.Length);");
            if (argument.CopyIn)
            {
                writer.AppendLine($"d_{argument.Name}.CopyToDevice({argument.Name});");
            }
        }

        writer.AppendLine();
    }

    private static void EmitContextInitialization(SourceWriter writer, string methodName,
        EquatableArray<ModuleKernelArgument> arguments)
    {
        writer.AppendLine($"var ctx = new {NativeContextPrefix}{methodName}");
        using (writer.Scope())
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                writer.Append(argument.IsArray
                    ? $"{argument.Name} = d_{argument.Name}.DevicePointer"
                    : $"{argument.Name} = {argument.Name}");
                writer.AppendLine(i < arguments.Length - 1 ? "," : string.Empty);
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void EmitKernelLaunch(SourceWriter writer, string methodName, ModuleKernelLaunchPlan plan,
        bool packContext)
    {
        writer.AppendLine("unsafe");
        using (writer.Scope())
        {
            if (packContext)
            {
                writer.AppendLine("var kernelParams = stackalloc void*[1];");
                writer.AppendLine($"kernelParams[0] = {AddressOf}<{NativeContextPrefix}{methodName}>(ref ctx);");
            }
            else
            {
                EmitUnpackedKernelParameters(writer, plan.Arguments);
            }

            writer.AppendLine();
            EmitCuLaunchKernelCall(writer, plan.Launch);
        }

        EmitLaunchSynchronization(writer, plan.Launch);
    }

    // var p_<name> = <name or d_<name>.DevicePointer>; ... kernelParams[<i>] = AddressOf<<type>>(ref p_<name>);
    // [WHY] The address of a value goes through AddressOf, which only accepts unmanaged types: a class written in a
    // YAML binding, which the reader cannot tell from a struct, fails to compile there instead of reaching the kernel
    // as the host address of the reference. A pointer cannot be a type argument and is never a reference, so its local
    // is passed by '&'.
    private static void EmitUnpackedKernelParameters(SourceWriter writer,
        EquatableArray<ModuleKernelArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            writer.AppendLine(argument.IsArray
                ? $"var p_{argument.Name} = d_{argument.Name}.DevicePointer;"
                : $"var p_{argument.Name} = {argument.Name};");
        }

        if (arguments.Length == 0)
        {
            writer.AppendLine("void** kernelParams = null;");
            return;
        }

        writer.AppendLine($"var kernelParams = stackalloc void*[{arguments.Length}];");
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            switch (argument.Kind)
            {
                case ModuleKernelParameterKind.Array:
                    writer.AppendLine(
                        $"kernelParams[{i}] = {AddressOf}<{HostTypeNames.IntPtr}>(ref p_{argument.Name});");
                    break;
                case ModuleKernelParameterKind.Pointer:
                    writer.AppendLine($"kernelParams[{i}] = &p_{argument.Name};");
                    break;
                default:
                    writer.AppendLine($"kernelParams[{i}] = {AddressOf}<{argument.TypeName}>(ref p_{argument.Name});");
                    break;
            }
        }
    }

    private static void EmitCuLaunchKernelCall(SourceWriter writer, ModuleLaunchConfiguration launch)
    {
        writer.AppendLine($"{ThrowIfError}({HostTypeNames.CudaDriverApi}.cuLaunchKernel(");
        writer.AppendLine("kernel,");
        writer.AppendLine($"{launch.GridDimX ?? "1"}, {launch.GridDimY ?? "1"}, {launch.GridDimZ ?? "1"},");
        writer.AppendLine($"{launch.BlockDimX ?? "1"}, {launch.BlockDimY ?? "1"}, {launch.BlockDimZ ?? "1"},");
        writer.AppendLine(
            $"{launch.SharedMemBytes ?? "0"}, {launch.Stream ?? Zero}, kernelParams, {Zero}));");
    }

    private static void EmitLaunchSynchronization(SourceWriter writer, ModuleLaunchConfiguration launch)
    {
        writer.AppendLine();
        writer.AppendLine(launch.Stream is { } stream
            ? $"{ThrowIfError}({HostTypeNames.CudaDriverApi}.cuStreamSynchronize({stream}));"
            : $"{ThrowIfError}({HostTypeNames.CudaDriverApi}.cuCtxSynchronize());");
    }

    private static void EmitBufferOutputs(SourceWriter writer, EquatableArray<ModuleKernelArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.IsArray && argument.CopyOut)
            {
                writer.AppendLine($"d_{argument.Name}.CopyToHost({argument.Name});");
            }
        }
    }
}
