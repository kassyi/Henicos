namespace Kassyi.GSharp.Compiler.NativeApi;

/// <summary>
/// A host-side parameter of a generated kernel launcher.
/// </summary>
/// <param name="Name">The C# parameter name.</param>
/// <param name="TypeName">The C# type as written in the launcher signature.</param>
/// <param name="ArrayElementTypeName">The element type when the parameter is a managed array; otherwise <see langword="null"/>.</param>
/// <param name="CopyIn">Whether the array is copied to the device before the launch.</param>
/// <param name="CopyOut">Whether the array is copied back to the host after the launch.</param>
internal readonly record struct KernelLaunchParameter(
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
/// </remarks>
internal static class KernelLaunchEmitter
{
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
        ImmutableArray<KernelLaunchParameter> parameters)
    {
        writer.AppendLine("[StructLayout(LayoutKind.Sequential)]");
        writer.AppendLine($"private struct __NativeContext_{methodName}");
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
    public static void EmitLaunchBody(
        SourceWriter writer,
        string moduleName,
        string entryPoint,
        string methodName,
        ImmutableArray<KernelLaunchParameter> parameters,
        bool packContext)
    {
        writer.AppendLine("using var scope = new GpuKernelLoader.CudaContextScope();");
        writer.Append("var kernel = GpuKernelLoader.LoadAotKernel(s_fatbin_");
        AppendSanitizedIdentifier(writer, moduleName);
        writer.Append(".Value, ");
        AppendStringLiteral(writer, entryPoint);
        writer.AppendLine(");");
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

    private static void EmitBufferAllocations(SourceWriter writer, ImmutableArray<KernelLaunchParameter> parameters)
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
        ImmutableArray<KernelLaunchParameter> parameters)
    {
        writer.AppendLine($"var ctx = new __NativeContext_{methodName}");
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

                var isLast = true;
                for (var j = i + 1; j < parameters.Length; j++)
                {
                    if (!IsLaunchParameter(parameters[j].Name))
                    {
                        isLast = false;
                        break;
                    }
                }

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

    private static void EmitKernelLaunch(SourceWriter writer, ImmutableArray<KernelLaunchParameter> parameters,
        bool packContext)
    {
        var kernelArgsCount = 0;
        foreach (var param in parameters)
        {
            if (!IsLaunchParameter(param.Name))
            {
                kernelArgsCount++;
            }
        }

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
            var stream = GetLaunchParamOrValue(parameters, "stream", "IntPtr.Zero");

            writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(");
            writer.AppendLine("kernel,");
            writer.AppendLine($"{gridX}, {gridY}, {gridZ},");
            writer.AppendLine($"{blockX}, {blockY}, {blockZ},");
            writer.AppendLine($"{sharedMem}, {stream}, kernelParams, IntPtr.Zero));");
        }

        writer.AppendLine();
        writer.AppendLine("CudaException.ThrowIfError(CudaDriverApi.cuCtxSynchronize());");
    }

    private static void EmitBufferOutputs(SourceWriter writer, ImmutableArray<KernelLaunchParameter> parameters)
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

    private static string GetLaunchParamOrValue(ImmutableArray<KernelLaunchParameter> parameters, string name,
        string defaultValue)
    {
        foreach (var p in parameters)
        {
            if (p.Name == name)
            {
                return name;
            }
        }

        return defaultValue;
    }
}
