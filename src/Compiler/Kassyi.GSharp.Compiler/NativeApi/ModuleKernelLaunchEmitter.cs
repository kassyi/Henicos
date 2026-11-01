namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// A host-side parameter of a generated kernel launcher.
/// </summary>
/// <param name="Name">The C# parameter name.</param>
/// <param name="TypeName">The C# type as written in the launcher signature.</param>
/// <param name="ArrayElementTypeName">The element type when the parameter is a managed array; otherwise <see langword="null"/>.</param>
/// <param name="CopyIn">Whether the array is copied to the device before the launch.</param>
/// <param name="CopyOut">Whether the array is copied back to the host after the launch.</param>
internal readonly record struct ModuleKernelLaunchParameter(
    string Name,
    string TypeName,
    string? ArrayElementTypeName,
    bool CopyIn,
    bool CopyOut)
{
    public bool IsArray => ArrayElementTypeName is not null;
}

/// <summary>
/// Emits launcher bodies that load a kernel from a fatbin module next to the application and launch it
/// through the CUDA driver API. Shared by <c>[CudaImport]</c> and YAML <c>moduleName</c> bindings.
/// </summary>
/// <remarks>
/// Parameters named <c>gridDimX/Y/Z</c>, <c>blockDimX/Y/Z</c>, <c>sharedMemBytes</c> and <c>stream</c> are launch
/// configuration and are not passed to the kernel. Managed arrays are staged through <c>GpuBuffer&lt;T&gt;</c>.
/// When a stream is given, synchronization is scoped to that stream.
/// </remarks>
internal static class ModuleKernelLaunchEmitter
{
    // The launcher-local struct that packs the kernel arguments for cuLaunchKernel; followed by the method name.
    private const string NativeContextPrefix = "__gsharp_native_context_";

    public static bool IsLaunchParameter(string name) =>
        name is "gridDimX" or "gridDimY" or "gridDimZ"
            or "blockDimX" or "blockDimY" or "blockDimZ"
            or "sharedMemBytes" or "stream";

    public static void EmitModuleCacheField(SourceWriter writer, string moduleName)
    {
        writer.Append("private static readonly Lazy<byte[]> s_fatbin_");
        AppendSanitizedIdentifier(writer, moduleName);
        writer.Append(" = new(() => System.IO.File.ReadAllBytes(System.IO.Path.Combine(AppContext.BaseDirectory, ");
        AppendStringLiteral(writer, moduleName);
        writer.AppendLine(")));");
        writer.AppendLine();
    }

    public static void EmitNativeContextStruct(SourceWriter writer, string methodName,
        ImmutableArray<ModuleKernelLaunchParameter> parameters)
    {
        writer.AppendLine("[StructLayout(LayoutKind.Sequential)]");
        writer.AppendLine($"private struct {NativeContextPrefix}{methodName}");
        using (writer.Scope())
        {
            foreach (var param in parameters)
            {
                if (IsLaunchParameter(param.Name))
                {
                    continue;
                }

                writer.AppendLine(param.IsArray
                    ? $"public IntPtr {param.Name};"
                    : $"public {param.TypeName} {param.Name};");
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
    /// <param name="parameters">The marshaled kernel parameters.</param>
    /// <param name="packContext">Whether struct parameters are packed into a generated launch context struct.</param>
    public static void EmitLaunchBody(
        SourceWriter writer,
        string moduleName,
        string entryPoint,
        string methodName,
        string launcherName,
        ImmutableArray<ModuleKernelLaunchParameter> parameters,
        bool packContext)
    {
        writer.AppendLine("using var scope = new GpuKernelLoader.CudaContextScope();");
        writer.Append("var kernel = GpuKernelLoader.LoadAotKernel(s_fatbin_");
        AppendSanitizedIdentifier(writer, moduleName);
        writer.Append(".Value, ");
        AppendStringLiteral(writer, entryPoint);
        writer.AppendLine(");");

        // [WHY] sharedMemBytes above 48 KB needs the function's opt-in, and a request the device cannot hold is
        // rejected with the launcher's name instead of the driver's bare CUDA_ERROR_INVALID_VALUE, as in
        // CudaHostLaunchEmitter (#545, #820). Without the parameter the launch requests no dynamic shared memory.
        if (HasLaunchParameter(parameters, "sharedMemBytes"))
        {
            writer.Append("global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare(kernel, sharedMemBytes, ");
            AppendStringLiteral(writer, launcherName);
            writer.AppendLine(");");
        }

        writer.AppendLine();

        EmitBufferAllocations(writer, parameters);
        if (packContext)
        {
            EmitContextInitialization(writer, methodName, parameters);
        }

        EmitKernelLaunch(writer, parameters, packContext);
        EmitBufferOutputs(writer, parameters);
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

    public static void AppendSanitizedIdentifier(SourceWriter writer, string name)
    {
        foreach (var c in name)
        {
            writer.Append(c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' ? c : '_');
        }
    }

    private static void EmitBufferAllocations(SourceWriter writer, ImmutableArray<ModuleKernelLaunchParameter> parameters)
    {
        foreach (var param in parameters)
        {
            if (IsLaunchParameter(param.Name) || !param.IsArray)
            {
                continue;
            }

            writer.AppendLine($"using var d_{param.Name} = new GpuBuffer<{param.ArrayElementTypeName}>({param.Name}.Length);");
            if (param.CopyIn)
            {
                writer.AppendLine($"d_{param.Name}.CopyToDevice({param.Name});");
            }
        }

        writer.AppendLine();
    }

    private static void EmitContextInitialization(SourceWriter writer, string methodName,
        ImmutableArray<ModuleKernelLaunchParameter> parameters)
    {
        writer.AppendLine($"var ctx = new {NativeContextPrefix}{methodName}");
        using (writer.Scope())
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                if (IsLaunchParameter(param.Name))
                {
                    continue;
                }

                writer.Append(param.IsArray
                    ? $"{param.Name} = d_{param.Name}.DevicePointer"
                    : $"{param.Name} = {param.Name}");

                var isLast = parameters
                    .AsStackEnumerable()
                    .Skip(i + 1)
                    .All(static p => IsLaunchParameter(p.Name));

                if (!isLast)
                {
                    writer.AppendLine(",");
                }
                else
                {
                    writer.AppendLine();
                }
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void EmitKernelLaunch(SourceWriter writer, ImmutableArray<ModuleKernelLaunchParameter> parameters,
        bool packContext)
    {
        var kernelArgsCount = parameters
            .AsStackEnumerable()
            .Count(static param => !IsLaunchParameter(param.Name));

        var stream = GetLaunchParamOrValue(parameters, "stream", "IntPtr.Zero");

        writer.AppendLine("unsafe");
        using (writer.Scope())
        {
            if (packContext)
            {
                writer.AppendLine("var kernelParams = stackalloc void*[1];");
                writer.AppendLine("kernelParams[0] = &ctx;");
            }
            else
            {
                foreach (var param in parameters)
                {
                    if (IsLaunchParameter(param.Name))
                    {
                        continue;
                    }

                    writer.AppendLine(param.IsArray
                        ? $"var p_{param.Name} = d_{param.Name}.DevicePointer;"
                        : $"var p_{param.Name} = {param.Name};");
                }

                if (kernelArgsCount > 0)
                {
                    writer.AppendLine($"var kernelParams = stackalloc void*[{kernelArgsCount}];");
                    var paramIndex = 0;
                    foreach (var param in parameters)
                    {
                        if (IsLaunchParameter(param.Name))
                        {
                            continue;
                        }

                        writer.AppendLine($"kernelParams[{paramIndex}] = &p_{param.Name};");
                        paramIndex++;
                    }
                }
                else
                {
                    writer.AppendLine("void** kernelParams = null;");
                }
            }

            writer.AppendLine();

            var gridX = GetLaunchParamOrValue(parameters, "gridDimX", "1");
            var gridY = GetLaunchParamOrValue(parameters, "gridDimY", "1");
            var gridZ = GetLaunchParamOrValue(parameters, "gridDimZ", "1");
            var blockX = GetLaunchParamOrValue(parameters, "blockDimX", "1");
            var blockY = GetLaunchParamOrValue(parameters, "blockDimY", "1");
            var blockZ = GetLaunchParamOrValue(parameters, "blockDimZ", "1");
            var sharedMem = GetLaunchParamOrValue(parameters, "sharedMemBytes", "0");

            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(");
            writer.AppendLine("kernel,");
            writer.AppendLine($"{gridX}, {gridY}, {gridZ},");
            writer.AppendLine($"{blockX}, {blockY}, {blockZ},");
            writer.AppendLine($"{sharedMem}, {stream}, kernelParams, IntPtr.Zero));");
        }

        writer.AppendLine();
        if (HasLaunchParameter(parameters, "stream"))
        {
            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(stream));");
        }
        else
        {
            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());");
        }
    }

    private static void EmitBufferOutputs(SourceWriter writer, ImmutableArray<ModuleKernelLaunchParameter> parameters)
    {
        foreach (var param in parameters)
        {
            if (IsLaunchParameter(param.Name) || !param.IsArray || !param.CopyOut)
            {
                continue;
            }

            writer.AppendLine($"d_{param.Name}.CopyToHost({param.Name});");
        }
    }

    private static bool HasLaunchParameter(ImmutableArray<ModuleKernelLaunchParameter> parameters, string name) =>
        parameters
            .AsStackEnumerable()
            .Any(name, static (p, target) => p.Name == target);

    private static string GetLaunchParamOrValue(ImmutableArray<ModuleKernelLaunchParameter> parameters, string name,
        string defaultValue) =>
        HasLaunchParameter(parameters, name) ? name : defaultValue;
}
