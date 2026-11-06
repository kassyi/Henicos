using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits the CUDA host launch members of a kernel wrapper class and the per-assembly module context.</summary>
internal static class CudaHostLaunchEmitter
{
    /// <summary>Emits the function pointer field, warmup method and launch wrappers of one kernel.</summary>
    public static void WriteKernelMembers(SourceWriter writer, in KernelEmission kernel, string assemblyName, bool isCheckedBuild = false)
    {
        WriteFunctionField(writer, kernel, assemblyName);
        WrapperEmitter.WriteLayoutCheckedField(writer, kernel.Host.CudaLayoutGuards, kernel.MemberKey);
        WriteWarmupFunctionPointerMethod(writer, kernel);
        WriteLaunchMethod(writer, kernel, false, isCheckedBuild);
        WriteLaunchMethod(writer, kernel, true, isCheckedBuild);
        if (kernel.IsOverloaded)
        {
            WriteTypedPointerLaunchMethod(writer, kernel);
        }
    }

    private static void WriteLaunchMethod(SourceWriter writer, in KernelEmission kernel, bool isDevicePtr, bool isCheckedBuild)
    {
        writer.AppendLine(
            "#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type");
        WrapperEmitter.WriteLaunchMethodSignature(writer, kernel, isDevicePtr);
        using (writer.Scope())
        {
            if (!isDevicePtr)
            {
                WrapperEmitter.WriteLayoutGuards(writer, kernel.Host.CudaLayoutGuards, kernel.MemberKey);
                WrapperEmitter.WriteOutParameterDefaults(writer, kernel.Host);
                WrapperEmitter.WriteArrayArgumentChecks(writer, kernel.Host);
            }

            AutoResultEmitter.WriteAllocation(writer, kernel.Host);

            writer.AppendLine(
                $"using (var {GSharpInternalNames.Launcher.CudaContextScope} = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())");
            using (writer.Scope())
            {
                WriteCudaKernelCompilationCall(writer, kernel);
                var sharing = ArraySharingPlan.ForCuda(kernel.Host, isDevicePtr);
                KernelLaunchEmitter.WriteVariablesInitialization(writer, kernel.Host, sharing, isDevicePtr);

                writer.AppendLine("try");
                using (writer.Scope())
                {
                    KernelLaunchEmitter.WriteMarshalAndLaunchKernel(writer, kernel.Host, sharing, isDevicePtr, isCheckedBuild);
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    KernelLaunchEmitter.WriteCleanup(writer, kernel.Host, sharing, isDevicePtr);
                }
            }

            AutoResultEmitter.WriteReturn(writer, kernel.Host);
        }

        writer.AppendLine("#pragma warning restore CS8500");
    }

    // The public Launch{Name}Ptr of an overloaded kernel: device pointers are typed GpuBuffer<T>, so C# selects the
    // overload, and the wrapper forwards their device pointers to the private IntPtr implementation.
    private static void WriteTypedPointerLaunchMethod(SourceWriter writer, in KernelEmission kernel)
    {
        writer.AppendGeneratedCodeAttributes();
        writer.Append("public static unsafe ");
        WrapperEmitter.WriteLaunchReturnType(writer, kernel.Host);
        writer.Append(KernelHostNames.GetTypedPointerLaunchName(kernel.Host));
        using (writer.Parentheses())
        {
            foreach (var p in kernel.Host.Parameters)
            {
                if (p.TryAppendTypedBuffer(writer))
                {
                    var isOptional = p.HasExplicitDefault;
                    writer.Append(isOptional ? "? " : " ").Append(p.Identifier);
                    if (isOptional)
                    {
                        writer.Append(" = null");
                    }
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
                    writer.Append(p.HasExplicitDefault ? "?.DevicePointer ?? global::System.IntPtr.Zero" : ".DevicePointer");
                }

                writer.Append(", ");
            }

            writer.Append(KernelHostNames.LaunchOptionNames);
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    // private static readonly CudaKernelFunction <field> = new CudaKernelFunction("<kernel>", <module context>.GetFatbin);
    private static void WriteFunctionField(SourceWriter writer, in KernelEmission kernel, string assemblyName)
    {
        const string FunctionType = "global::Kassyi.GSharp.Runtime.Aot.CudaKernelFunction";
        var field = KernelHostNames.GetFunctionPointerFieldName(kernel.MemberKey);
        var moduleContextName = AotHostNames.GetModuleContextName(assemblyName);
        writer.Append($"private static readonly {FunctionType} {field} = new {FunctionType}");
        using (writer.Parentheses())
        {
            writer.Append($"\"{kernel.Host.KernelName}\", ");
            writer.Append($"global::Kassyi.GSharp.Generated.{moduleContextName}.GetFatbin");
        }

        writer.AppendLine(";");
    }

    private static void WriteWarmupFunctionPointerMethod(SourceWriter writer, in KernelEmission kernel)
    {
        writer.Append("/// <summary>Preloads the CUDA module and resolves the function pointer for this kernel ");
        writer.AppendLine("in the current CUDA context.</summary>");
        writer.Append($"public static void {KernelHostNames.GetWarmupMethodName(kernel.MemberKey)}() => ");
        WriteResolveFunction(writer, kernel);
        writer.AppendLine(";");
        writer.AppendLine();
    }

    // The function is resolved per launch: a CUfunction belongs to the context its module was loaded in (#1307). Both
    // names are class-qualified, so a kernel parameter of the same name cannot shadow them.
    // <class>.<field>.Resolve(<class>.s_errorSites)
    private static void WriteResolveFunction(SourceWriter writer, in KernelEmission kernel)
    {
        var className = kernel.Host.ClassQualifiedName;
        writer.Append($"{className}.{KernelHostNames.GetFunctionPointerFieldName(kernel.MemberKey)}.Resolve");
        using (writer.Parentheses())
        {
            if (!kernel.ErrorSites.IsDefaultOrEmpty)
            {
                writer.Append($"{className}.s_errorSites");
            }
        }
    }

    private static void WriteCudaKernelCompilationCall(SourceWriter writer, in KernelEmission kernel)
    {
        const string Func = GSharpInternalNames.Launcher.FunctionHandle;
        writer.Append($"global::System.IntPtr {Func} = ");
        WriteResolveFunction(writer, kernel);
        writer.AppendLine(";");

        // [WHY] sharedMemBytes above 48 KB needs the function's opt-in; a request the device cannot hold is rejected
        // with the kernel's name instead of the driver's bare CUDA_ERROR_INVALID_VALUE (#545)
        writer.AppendLine(
            $"global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare({Func}, sharedMemBytes, {SymbolDisplay.FormatLiteral(kernel.Host.MessageName, quote: true)});");
        writer.AppendLine();
    }

    public static string GenerateModuleContextClass(string assemblyName,
        EquatableArray<KernelWarmup> warmups)
    {
        var safeAssemblyName = AotHostNames.ToSafeIdentifier(assemblyName);
        var moduleContextName = AotHostNames.GetModuleContextName(assemblyName);

        var writer = new SourceWriter();
        writer.AppendGeneratedFileHeader();
        writer.AppendLine("""
            using System;
            using System.Threading;

            namespace Kassyi.GSharp.Generated
            {
            """);
        writer.AppendGeneratedCodeAttributes();
        writer.AppendLine($$"""
            public static class {{moduleContextName}}
            {
            private static byte[]? s_fatbin;

            /// <summary>
            /// Reads the fatbin module of this assembly; the file is read once.
            /// </summary>
            public static byte[] GetFatbin()
            {
            var fatbin = global::System.Threading.Volatile.Read(ref s_fatbin);
            if (fatbin is not null) return fatbin;
            var baseDir = global::System.IO.Path.GetDirectoryName(typeof({{moduleContextName}}).Assembly.Location) ?? global::System.AppContext.BaseDirectory;
            var fatbinPath = global::System.IO.Path.Combine(baseDir, "Kassyi.GSharp.Kernels_{{safeAssemblyName}}.gfatbin");
            if (!global::System.IO.File.Exists(fatbinPath))
            {
            throw new global::System.InvalidOperationException($"AOT kernel binary was not found at '{fatbinPath}'.");
            }

            fatbin = global::System.IO.File.ReadAllBytes(fatbinPath);
            return global::System.Threading.Interlocked.CompareExchange(ref s_fatbin, fatbin, null) ?? fatbin;
            }

            /// <summary>
            /// Loads the fatbin module of this assembly into the current CUDA context if it is not loaded there yet.
            /// </summary>
            public static global::System.IntPtr GetOrLoadModule() => global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.LoadModule(GetFatbin());

            /// <summary>
            /// Preloads the module and all function pointers for kernels in this assembly to eliminate lazy-loading latency.
            /// </summary>
            public static void Warmup()
            {
            // Force load the module first
            GetOrLoadModule();
            """);

        foreach (var warmup in warmups)
        {
            writer.AppendLine($"{warmup.Class.FullyQualifiedName}.{KernelHostNames.GetWarmupMethodName(warmup.MemberKey)}();");
        }

        writer.AppendLine("}}}");

        return writer.ToString();
    }
}
