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

    private const string ModuleKernelArguments = HostTypeNames.ModuleKernelArguments;

    private const string AddressOf = ModuleKernelArguments + ".AddressOf";

    private const string Zero = HostTypeNames.IntPtr + ".Zero";

    private const string ThrowIfError = HostTypeNames.CudaException + ".ThrowIfError";

    public static void EmitModuleCacheField(SourceWriter writer, string moduleName)
    {
        writer.Append("private static readonly global::System.Lazy<byte[]> s_fatbin_");
        writer.Append(AotModuleNames.ToIdentifierPart(moduleName));
        writer.Append(" = new(() => global::System.IO.File.ReadAllBytes(global::System.IO.Path.Combine(");
        writer.Append("global::System.AppContext.BaseDirectory, ");
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
                writer.AppendLine(argument.IsStaged
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
        WriteNullChecks(writer, plan.Arguments);
        WriteLaunchLimitCheck(writer, launcherName, launch);
        writer.AppendLine($"using {HostTypeNames.CudaContextScope} scope = new();");
        writer.Append($"{HostTypeNames.IntPtr} kernel = {HostTypeNames.GpuKernelLoader}.LoadAotKernel(s_fatbin_");
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

    // if (<name> is null) { throw new global::System.ArgumentNullException("<name>"); }
    // [WHY] Only an array declared nullable (T[]?, T[,]?) reaches the kernel as a null pointer; null for another array
    // is rejected before anything is copied or launched, as the launchers of [GpuKernel] methods reject it
    private static void WriteNullChecks(SourceWriter writer, EquatableArray<ModuleKernelArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            if (!argument.IsArray || argument.AllowsNull)
            {
                continue;
            }

            writer.Append("if ");
            using (writer.Parentheses())
            {
                writer.Append($"{argument.Name} is null");
            }

            using (writer.Scope())
            {
                writer.Append($"throw new {HostTypeNames.ArgumentNullException}(");
                AppendStringLiteral(writer, argument.Name);
                writer.AppendLine(");");
            }
        }
    }

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

    // Array: using GpuBuffer<<element>> d_<name> = new(<name>.Length); d_<name>.CopyToDevice(<elements of name>);
    // Nullable array: using GpuBuffer<<element>>? d_<name> = <name> is null ? null : new(<name>.Length);
    //                 d_<name>?.CopyToDevice(<elements of name>);
    // By reference: [<type> h_<name> = default;] using GpuBuffer<<type>> d_<name> = new(1);
    //               d_<name>.CopyToDevice(new ReadOnlySpan<<type>>(in <name> or h_<name>));
    // [WHY] A null nullable array is passed as a null pointer. An 'out' value sends default from a local of the
    // launcher (h_<name>): C# cannot read the parameter before it is assigned, the kernel starts from a known value
    // instead of what the device memory held, and the caller's variable changes only in the write-back, so a launch
    // that throws leaves it as it was and a 'ref' parameter that aliases it is copied in with the caller's value.
    private static void EmitBufferAllocations(SourceWriter writer, EquatableArray<ModuleKernelArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            var name = argument.Name;
            var buffer = $"{HostTypeNames.GpuBuffer}<{argument.TypeName}>";
            switch (argument.Kind)
            {
                case ModuleKernelParameterKind.Array or ModuleKernelParameterKind.MultidimensionalArray:
                    writer.AppendLine(argument.AllowsNull
                        ? $"using {buffer}? d_{name} = {name} is null ? null : new({name}.Length);"
                        : $"using {buffer} d_{name} = new({name}.Length);");
                    if (argument.CopyIn)
                    {
                        writer.Append($"d_{name}{GetAccess(argument)}CopyToDevice(");
                        WriteHostElements(writer, argument);
                        writer.AppendLine(");");
                    }

                    break;
                case ModuleKernelParameterKind.ByReference:
                    var host = GetHostValue(argument);
                    writer.AppendLineIf(!argument.CopyIn, $"{argument.TypeName} {host} = default;");
                    writer.AppendLine($"using {buffer} d_{name} = new(1);");
                    writer.AppendLine(
                        $"d_{name}.CopyToDevice(new {HostTypeNames.ReadOnlySpan}<{argument.TypeName}>(in {host}));");
                    break;
            }
        }

        writer.AppendLine();
    }

    // <name>, or ModuleKernelArguments.Flatten<<element>>(<name>): the elements of an array as one span
    private static void WriteHostElements(SourceWriter writer, ModuleKernelArgument argument) =>
        writer.Append(argument.Kind == ModuleKernelParameterKind.MultidimensionalArray
            ? $"{ModuleKernelArguments}.Flatten<{argument.TypeName}>({argument.Name})"
            : argument.Name);

    // d_<name>?.DevicePointer ?? IntPtr.Zero for a nullable array, d_<name>.DevicePointer otherwise
    private static string GetDevicePointer(ModuleKernelArgument argument) => argument.AllowsNull
        ? $"d_{argument.Name}?.DevicePointer ?? {Zero}"
        : $"d_{argument.Name}.DevicePointer";

    // The host storage of a value passed by reference: the parameter, or the launcher local h_<name> of an 'out' value
    private static string GetHostValue(ModuleKernelArgument argument) =>
        argument.CopyIn ? argument.Name : $"h_{argument.Name}";

    // The member access on the buffer of <argument>: '?.' when a nullable array leaves it null
    private static string GetAccess(ModuleKernelArgument argument) => argument.AllowsNull ? "?." : ".";

    private static void EmitContextInitialization(SourceWriter writer, string methodName,
        EquatableArray<ModuleKernelArgument> arguments)
    {
        writer.AppendLine($"{NativeContextPrefix}{methodName} ctx = new()");
        using (writer.Scope())
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                writer.Append(argument.IsStaged
                    ? $"{argument.Name} = {GetDevicePointer(argument)}"
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
                writer.AppendLine("void** kernelParams = stackalloc void*[1];");
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

    // <type> p_<name> = <name or the device pointer of d_<name>>; ...
    // kernelParams[<i>] = AddressOf<<type>>(ref p_<name>);
    // [WHY] The address of a value goes through AddressOf, which only accepts unmanaged types: a class written in a
    // YAML binding, which the reader cannot tell from a struct, fails to compile there instead of reaching the kernel
    // as the host address of the reference. A pointer cannot be a type argument and is never a reference, so its local
    // is passed by '&'.
    private static void EmitUnpackedKernelParameters(SourceWriter writer,
        EquatableArray<ModuleKernelArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            writer.AppendLine(argument.IsStaged
                ? $"{HostTypeNames.IntPtr} p_{argument.Name} = {GetDevicePointer(argument)};"
                : $"{argument.TypeName} p_{argument.Name} = {argument.Name};");
        }

        if (arguments.Length == 0)
        {
            writer.AppendLine("void** kernelParams = null;");
            return;
        }

        writer.AppendLine($"void** kernelParams = stackalloc void*[{arguments.Length}];");
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            if (argument.Kind == ModuleKernelParameterKind.Pointer)
            {
                writer.AppendLine($"kernelParams[{i}] = &p_{argument.Name};");
                continue;
            }

            var type = argument.IsStaged ? HostTypeNames.IntPtr : argument.TypeName;
            writer.AppendLine($"kernelParams[{i}] = {AddressOf}<{type}>(ref p_{argument.Name});");
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

    // Array: d_<name>[?].CopyToHost(<elements of name>);
    // By reference: d_<name>.CopyToHost(new Span<<type>>(ref <name> or h_<name>)); [<name> = h_<name>;]
    private static void EmitBufferOutputs(SourceWriter writer, EquatableArray<ModuleKernelArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            if (!argument.IsStaged || !argument.CopyOut)
            {
                continue;
            }

            var name = argument.Name;
            if (argument.IsArray)
            {
                writer.Append($"d_{name}{GetAccess(argument)}CopyToHost(");
                WriteHostElements(writer, argument);
                writer.AppendLine(");");
            }
            else
            {
                var host = GetHostValue(argument);
                writer.AppendLine($"d_{name}.CopyToHost(new {HostTypeNames.Span}<{argument.TypeName}>(ref {host}));");
                writer.AppendLineIf(!argument.CopyIn, $"{name} = {host};");
            }
        }
    }
}
