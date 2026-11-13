namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// Emits launcher bodies that load a kernel from a fatbin module next to the application and launch it
/// through the CUDA driver API. Shared by <c>[CudaImport]</c> and YAML <c>moduleName</c> bindings.
/// </summary>
/// <remarks>
/// Writes a <see cref="ModuleKernelLaunchPlan"/> and decides nothing: the planners (<see cref="CudaImportPlanner"/>,
/// <see cref="ModuleBindingPlanner"/>) choose which parameters are kernel arguments, how each is passed and which set
/// the launch. Managed arrays are staged through <c>GpuBuffer&lt;T&gt;</c>. When a stream is given, synchronization is
/// scoped to that stream. Every local and member it declares is a <c>__gsharp_</c> name numbered by position, and every
/// type it names is written from <c>global::</c>, so neither a parameter name nor a type of the user's namespace can
/// change the generated code.
/// </remarks>
internal static class ModuleKernelLaunchEmitter
{
    private const string ModuleKernelArguments = HostTypeNames.ModuleKernelArguments;

    private const string AddressOf = ModuleKernelArguments + ".AddressOf";

    private const string Zero = HostTypeNames.IntPtr + ".Zero";

    private const string ThrowIfError = HostTypeNames.CudaException + ".ThrowIfError";

    private const string CudaContextScope = GSharpInternalNames.Launcher.CudaContextScope;

    private const string FunctionHandle = GSharpInternalNames.Launcher.FunctionHandle;

    private const string KernelParams = GSharpInternalNames.Launcher.KernelParams;

    private const string NativeContext = GSharpInternalNames.Launcher.NativeContext;

    /// <summary>
    /// The struct that packs the arguments of the method at <paramref name="index"/> among the methods of a class,
    /// <c>__gsharp_native_context_{index}</c>; numbered, so that overloads get one each.
    /// </summary>
    public static string GetNativeContextTypeName(int index) =>
        GSharpInternalNames.Launcher.NativeContextTypePrefix + index.ToString(CultureInfo.InvariantCulture);

    // private static readonly global::System.Lazy<byte[]> <field> = new(() => global::System.IO.File.ReadAllBytes(
    // global::System.IO.Path.Combine(global::System.AppContext.BaseDirectory, "<module>")));
    public static void EmitModuleCacheField(SourceWriter writer, string fieldName, string moduleName)
    {
        writer.Append($"private static readonly {HostTypeNames.Lazy}<byte[]> {fieldName} = new(() => ");
        writer.Append($"{HostTypeNames.File}.ReadAllBytes({HostTypeNames.Path}.Combine({HostTypeNames.AppContext}");
        writer.AppendLine($".BaseDirectory, {Literal(moduleName)})));");
        writer.AppendLine();
    }

    public static void EmitNativeContextStruct(SourceWriter writer, string typeName, ModuleKernelLaunchPlan plan)
    {
        writer.AppendLine($"[{HostTypeNames.StructLayout}({HostTypeNames.LayoutKind}.Sequential)]");
        writer.AppendLine($"private struct {typeName}");
        using (writer.Scope())
        {
            foreach (var argument in plan.Arguments)
            {
                writer.AppendLine(argument.IsStaged
                    ? $"public {HostTypeNames.IntPtr} {Identifier(argument)};"
                    : $"public {argument.TypeName} {Identifier(argument)};");
            }
        }

        writer.AppendLine();
    }

    /// <summary>
    /// Emits the statements of a launcher body (without the enclosing braces).
    /// </summary>
    /// <param name="writer">The destination source writer.</param>
    /// <param name="moduleField">
    /// The field that holds the bytes of the module (<see cref="AotModuleNames.GetModuleFieldName"/>).
    /// </param>
    /// <param name="entryPoint">The mangled native kernel entry-point symbol.</param>
    /// <param name="launcherName">The launcher as <c>Type.Method</c>, named by the shared memory limit error.</param>
    /// <param name="plan">The kernel arguments and launch settings; it must have no problems.</param>
    /// <param name="contextTypeName">
    /// The struct that packs the kernel arguments into one (<see cref="GetNativeContextTypeName"/>), or
    /// <see langword="null"/> to pass each argument on its own.
    /// </param>
    public static void EmitLaunchBody(
        SourceWriter writer,
        string moduleField,
        string entryPoint,
        string launcherName,
        ModuleKernelLaunchPlan plan,
        string? contextTypeName)
    {
        if (!plan.CanLaunch)
        {
            throw new InvalidOperationException($"The launcher of '{launcherName}' has parameters it cannot pass.");
        }

        var launch = plan.Launch;
        WriteNullChecks(writer, plan.Arguments);
        WriteLaunchLimitCheck(writer, launcherName, launch);
        // [WHY] Every launcher local has an explicit type: in a class named 'var', 'var' names that class
        writer.AppendLine(
            $"using {HostTypeNames.CudaContextScope} {CudaContextScope} = new();");
        var entryPointLiteral = Literal(entryPoint);
        writer.AppendLine($"{HostTypeNames.IntPtr} {FunctionHandle} = {HostTypeNames.GpuKernelLoader}.LoadAotKernel(" +
            $"{moduleField}.Value, {entryPointLiteral});");

        // [WHY] sharedMemBytes above 48 KB needs the function's opt-in, and a request the device cannot hold is
        // rejected with the launcher's name instead of the driver's bare CUDA_ERROR_INVALID_VALUE, as in
        // CudaHostLaunchEmitter (#545, #820). Without the parameter the launch requests no dynamic shared memory.
        if (launch.SharedMemBytes is { } sharedMemBytes)
        {
            var launcherLiteral = Literal(launcherName);
            writer.AppendLine("global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare" +
                $"({FunctionHandle}, {sharedMemBytes}, {launcherLiteral});");
        }

        writer.AppendLine();

        EmitBufferAllocations(writer, plan.Arguments);
        if (!plan.ComparesAliases)
        {
            EmitLaunchAndOutputs(writer, contextTypeName, plan);
            return;
        }

        ModuleAliasGroupEmitter.EmitGroups(writer, plan);
        writer.AppendLine("try");
        using (writer.Scope())
        {
            EmitLaunchAndOutputs(writer, contextTypeName, plan);
        }

        writer.AppendLine("finally");
        using (writer.Scope())
        {
            ModuleAliasGroupEmitter.EmitFree(writer, plan);
        }
    }

    private static void EmitLaunchAndOutputs(SourceWriter writer, string? contextTypeName, ModuleKernelLaunchPlan plan)
    {
        if (contextTypeName is not null)
        {
            EmitContextInitialization(writer, contextTypeName, plan);
        }

        EmitKernelLaunch(writer, contextTypeName, plan);
        ModuleAliasGroupEmitter.EmitCopyBack(writer, plan);
        EmitBufferOutputs(writer, plan);
    }

    /// <summary>
    /// Emits a launcher body that throws <see cref="NotSupportedException"/> with <paramref name="message"/>, for a
    /// method whose parameters the launcher cannot pass (the analyzer reports the same message as an error).
    /// </summary>
    // throw new global::System.NotSupportedException("<message>");
    public static void EmitUnavailableBody(SourceWriter writer, string message) =>
        writer.AppendLine(
            $"throw new global::System.NotSupportedException({Literal(message)});");

    // if (<name> is null) { throw new global::System.ArgumentNullException("<name>"); }
    // [WHY] The name is a string literal: 'nameof' would bind to a parameter named 'nameof' (CS0149)
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
                writer.Append($"{Identifier(argument)} is null");
            }

            using (writer.Scope())
            {
                var name = Literal(argument.Name);
                writer.AppendLine($"throw new {HostTypeNames.ArgumentNullException}({name});");
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
            writer.Append(Literal(launcherName));
        }

        writer.AppendLine(";");
    }

    /// <summary>
    /// Returns <paramref name="value"/> as a C# string literal, every character a regular literal cannot hold escaped.
    /// Shared with <see cref="NativeBindingEmitter"/>.
    /// </summary>
    internal static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    // Array: using var <d> = new GpuBuffer<<element>>(<name>.Length); <d>.CopyToDevice(<elements of name>);
    // Nullable array: using var <d> = <name> is null ? null : new GpuBuffer<<element>>(<name>.Length);
    //                 <d>?.CopyToDevice(<elements of name>);
    // By reference: [var <out> = default(<type>);] using var <d> = new GpuBuffer<<type>>(1);
    //               <d>.CopyToDevice(new ReadOnlySpan<<type>>(in <name> or <out>));
    // (<d> is __gsharp_d_<ordinal>, <out> __gsharp_out_<ordinal>)
    // [WHY] A null nullable array is passed as a null pointer. An 'out' value sends default from a local of the
    // launcher (<out>): C# cannot read the parameter before it is assigned, the kernel starts from a known value
    // instead of what the device memory held, and the caller's variable changes only in the write-back, so a launch
    // that throws leaves it as it was and a 'ref' parameter that aliases it is copied in with the caller's value.
    private static void EmitBufferAllocations(SourceWriter writer, EquatableArray<ModuleKernelArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            var name = Identifier(argument);
            var device = GetDeviceBuffer(argument);
            var buffer = $"{HostTypeNames.GpuBuffer}<{argument.TypeName}>";
            switch (argument.Kind)
            {
                case ModuleKernelParameterKind.Array or ModuleKernelParameterKind.MultidimensionalArray:
                    writer.AppendLine(argument.AllowsNull
                        ? $"using {buffer}? {device} = {name} is null ? null : new({name}.Length);"
                        : $"using {buffer} {device} = new({name}.Length);");
                    if (argument.CopyIn)
                    {
                        writer.Append($"{device}{GetAccess(argument)}CopyToDevice(");
                        WriteHostElements(writer, argument);
                        writer.AppendLine(");");
                    }

                    break;
                case ModuleKernelParameterKind.ByReference:
                    var host = GetHostValue(argument);
                    var type = argument.TypeName;
                    writer.AppendLineIf(!argument.CopyIn, $"{type} {host} = default;");
                    writer.AppendLine($"using {buffer} {device} = new(1);");
                    writer.AppendLine(
                        $"{device}.CopyToDevice(new {HostTypeNames.ReadOnlySpan}<{argument.TypeName}>(in {host}));");
                    break;
            }
        }

        writer.AppendLine();
    }

    // <name>, or ModuleKernelArguments.Flatten<<element>>(<name>): the elements of an array as one span
    private static void WriteHostElements(SourceWriter writer, ModuleKernelArgument argument) =>
        writer.Append(argument.Kind == ModuleKernelParameterKind.MultidimensionalArray
            ? $"{ModuleKernelArguments}.Flatten<{argument.TypeName}>({Identifier(argument)})"
            : Identifier(argument));

    // The parameter as C# source names it (@in for 'in')
    internal static string Identifier(ModuleKernelArgument argument) => argument.Name.ToCSharpIdentifier();

    // The device buffer local of <argument>, __gsharp_d_<ordinal>
    private static string GetDeviceBuffer(ModuleKernelArgument argument) =>
        KernelHostNames.GetLocal(LauncherLocal.DevicePointer, argument.Ordinal);

    // <d>?.DevicePointer ?? IntPtr.Zero for a nullable array, <d>.DevicePointer otherwise; for an alias candidate,
    // its address in its group's region first: __gsharp_aliases?.IsMember(<k>) == true ? <group address> : <buffer>
    private static string GetDevicePointer(ModuleKernelLaunchPlan plan, ModuleKernelArgument argument)
    {
        var buffer = argument.AllowsNull
            ? $"{GetDeviceBuffer(argument)}?.DevicePointer ?? {Zero}"
            : $"{GetDeviceBuffer(argument)}.DevicePointer";
        var index = ModuleAliasGroupEmitter.IndexOf(plan, argument);
        if (index < 0)
        {
            return buffer;
        }

        var member = ModuleAliasGroupEmitter.MemberTest(index);
        return $"{member} ? {ModuleAliasGroupEmitter.DevicePointer(index)} : {buffer}";
    }

    // The host storage of a value passed by reference: the parameter, or the launcher local __gsharp_out_<ordinal> of
    // an 'out' value
    private static string GetHostValue(ModuleKernelArgument argument) =>
        argument.CopyIn ? Identifier(argument) : KernelHostNames.GetLocal(LauncherLocal.OutValue, argument.Ordinal);

    // The member access on the buffer of <argument>: '?.' when a nullable array leaves it null
    private static string GetAccess(ModuleKernelArgument argument) => argument.AllowsNull ? "?." : ".";

    private static void EmitContextInitialization(SourceWriter writer, string contextTypeName,
        ModuleKernelLaunchPlan plan)
    {
        var arguments = plan.Arguments;
        writer.AppendLine($"{contextTypeName} {NativeContext} = new()");
        using (writer.Scope())
        {
            for (var i = 0; i < arguments.Length; i++)
            {
                var argument = arguments[i];
                writer.Append(argument.IsStaged
                    ? $"{Identifier(argument)} = {GetDevicePointer(plan, argument)}"
                    : $"{Identifier(argument)} = {Identifier(argument)}");
                writer.AppendLine(i < arguments.Length - 1 ? "," : string.Empty);
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void EmitKernelLaunch(SourceWriter writer, string? contextTypeName, ModuleKernelLaunchPlan plan)
    {
        writer.AppendLine("unsafe");
        using (writer.Scope())
        {
            if (contextTypeName is not null)
            {
                writer.AppendLine($"void** {KernelParams} = stackalloc void*[1];");
                writer.AppendLine($"{KernelParams}[0] = {AddressOf}<{contextTypeName}>(ref {NativeContext});");
            }
            else
            {
                EmitUnpackedKernelParameters(writer, plan);
            }

            writer.AppendLine();
            EmitCuLaunchKernelCall(writer, plan.Launch);
        }

        EmitLaunchSynchronization(writer, plan.Launch);
    }

    // var <k> = <name or the device pointer of its buffer>; ... kernelParams[<i>] = AddressOf<<type>>(ref <k>);
    // (<k> is __gsharp_karg_<ordinal>)
    // [WHY] The address of a value goes through AddressOf, which only accepts unmanaged types: a class written in a
    // YAML binding, which the reader cannot tell from a struct, fails to compile there instead of reaching the kernel
    // as the host address of the reference. A pointer cannot be a type argument and is never a reference, so its local
    // is passed by '&'.
    private static void EmitUnpackedKernelParameters(SourceWriter writer, ModuleKernelLaunchPlan plan)
    {
        var arguments = plan.Arguments;
        foreach (var argument in arguments)
        {
            var local = KernelHostNames.GetLocal(LauncherLocal.KernelArgument, argument.Ordinal);
            writer.AppendLine(argument.IsStaged
                ? $"{HostTypeNames.IntPtr} {local} = {GetDevicePointer(plan, argument)};"
                : $"{argument.TypeName} {local} = {Identifier(argument)};");
        }

        if (arguments.Length == 0)
        {
            writer.AppendLine($"void** {KernelParams} = null;");
            return;
        }

        writer.AppendLine($"void** {KernelParams} = stackalloc void*[{arguments.Length}];");
        for (var i = 0; i < arguments.Length; i++)
        {
            var argument = arguments[i];
            var local = KernelHostNames.GetLocal(LauncherLocal.KernelArgument, argument.Ordinal);
            if (argument.Kind == ModuleKernelParameterKind.Pointer)
            {
                writer.AppendLine($"{KernelParams}[{i}] = &{local};");
                continue;
            }

            var type = argument.IsStaged ? HostTypeNames.IntPtr : argument.TypeName;
            writer.AppendLine($"{KernelParams}[{i}] = {AddressOf}<{type}>(ref {local});");
        }
    }

    private static void EmitCuLaunchKernelCall(SourceWriter writer, ModuleLaunchConfiguration launch)
    {
        writer.AppendLine($"{ThrowIfError}({HostTypeNames.CudaDriverApi}.cuLaunchKernel(");
        writer.AppendLine($"{FunctionHandle},");
        writer.AppendLine($"{launch.GridDimX ?? "1"}, {launch.GridDimY ?? "1"}, {launch.GridDimZ ?? "1"},");
        writer.AppendLine($"{launch.BlockDimX ?? "1"}, {launch.BlockDimY ?? "1"}, {launch.BlockDimZ ?? "1"},");
        writer.AppendLine(
            $"{launch.SharedMemBytes ?? "0"}, {launch.Stream ?? Zero}, {KernelParams}, {Zero}));");
    }

    private static void EmitLaunchSynchronization(SourceWriter writer, ModuleLaunchConfiguration launch)
    {
        writer.AppendLine();
        writer.AppendLine(launch.Stream is { } stream
            ? $"{ThrowIfError}({HostTypeNames.CudaDriverApi}.cuStreamSynchronize({stream}));"
            : $"{ThrowIfError}({HostTypeNames.CudaDriverApi}.cuCtxSynchronize());");
    }

    // [if (!(__gsharp_aliases?.IsMember(<k>) == true))]   (the group of a member copied it back)
    // Array: <d>[?].CopyToHost(<elements of name>);
    // By reference: <d>.CopyToHost(new Span<<type>>(ref <name> or <out>)); [<name> = <out>;]
    private static void EmitBufferOutputs(SourceWriter writer, ModuleKernelLaunchPlan plan)
    {
        foreach (var argument in plan.Arguments)
        {
            if (!argument.IsStaged || !argument.CopyOut)
            {
                continue;
            }

            var index = ModuleAliasGroupEmitter.IndexOf(plan, argument);
            writer.AppendLineIf(index >= 0, $"if (!({ModuleAliasGroupEmitter.MemberTest(index)}))");
            using var notMember = index >= 0 ? writer.Scope() : default;
            var name = Identifier(argument);
            var device = GetDeviceBuffer(argument);
            if (argument.IsArray)
            {
                writer.Append($"{device}{GetAccess(argument)}CopyToHost(");
                WriteHostElements(writer, argument);
                writer.AppendLine(");");
            }
            else
            {
                var host = GetHostValue(argument);
                writer.AppendLine($"{device}.CopyToHost(new {HostTypeNames.Span}<{argument.TypeName}>(ref {host}));");
                writer.AppendLineIf(!argument.CopyIn, $"{name} = {host};");
            }
        }
    }
}
