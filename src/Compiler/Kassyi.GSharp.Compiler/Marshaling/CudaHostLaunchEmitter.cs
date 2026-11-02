using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits the CUDA host launch members of a kernel wrapper class and the per-assembly module context.</summary>
internal static class CudaHostLaunchEmitter
{
    /// <summary>Emits the function pointer field, warmup method and launch wrappers of one kernel.</summary>
    public static void WriteKernelMembers(SourceWriter writer, in KernelEmission kernel, string assemblyName, bool isCheckedBuild = false)
    {
        writer.AppendLine($"private static IntPtr? s_hfunc_{kernel.MemberKey};");
        WrapperEmitter.WriteLayoutCheckedField(writer, kernel.Host.CudaLayoutGuards, kernel.MemberKey);
        WriteWarmupFunctionPointerMethod(writer, kernel, assemblyName);
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

    private const string LaunchOptionArguments =
        "gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes, hStream, macros";

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
        writer.Append("=> ").Append(KernelHostNames.GetPointerLaunchName(kernel));
        using (writer.Parentheses())
        {
            foreach (var p in kernel.Host.Parameters)
            {
                writer.Append(p.Identifier);
                if (p.BufferElementTypeName is not null)
                {
                    writer.Append(p.HasExplicitDefault ? "?.DevicePointer ?? IntPtr.Zero" : ".DevicePointer");
                }

                writer.Append(", ");
            }

            writer.Append(LaunchOptionArguments);
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void WriteWarmupFunctionPointerMethod(SourceWriter writer, in KernelEmission kernel,
        string assemblyName)
    {
        var key = kernel.MemberKey;
        writer.AppendLine(
            "/// <summary>Preloads the CUDA module and resolves the function pointer for this kernel.</summary>");
        writer.AppendLine($"public static void WarmupFunctionPointer{key}()");
        using (writer.Scope())
        {
            writer.AppendLine($"if (s_hfunc_{key}.HasValue) return;");
            writer.Append("IntPtr hModule = global::Kassyi.GSharp.Generated.");
            writer.Append(AotHostNames.GetModuleContextName(assemblyName));
            writer.AppendLine(".GetOrLoadModule();");
            writer.AppendLine(
                $"s_hfunc_{key} = global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.GetFunction(hModule, \"{kernel.Host.KernelName}\");");
        }

        writer.AppendLine();
    }

    private static void WriteCudaKernelCompilationCall(SourceWriter writer, in KernelEmission kernel)
    {
        var key = kernel.MemberKey;
        writer.AppendLine($"WarmupFunctionPointer{key}();");
        writer.AppendLine($"IntPtr hfunc = s_hfunc_{key}!.Value;");

        // [WHY] sharedMemBytes above 48 KB needs the function's opt-in; a request the device cannot hold is rejected
        // with the kernel's name instead of the driver's bare CUDA_ERROR_INVALID_VALUE (#545)
        writer.AppendLine(
            $"global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare(hfunc, sharedMemBytes, {SymbolDisplay.FormatLiteral(kernel.Host.MessageName, quote: true)});");
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
            private static IntPtr? s_hModule;
            private static readonly object s_lock = new object();

            /// <summary>
            /// Loads the fatbin module for this assembly into the CUDA driver if it hasn't been loaded yet.
            /// </summary>
            public static IntPtr GetOrLoadModule()
            {
            if (s_hModule.HasValue) return s_hModule.Value;
            lock (s_lock)
            {
            if (s_hModule.HasValue) return s_hModule.Value;

            var baseDir = System.IO.Path.GetDirectoryName(typeof({{moduleContextName}}).Assembly.Location) ?? System.AppContext.BaseDirectory;
            var fatbinPath = System.IO.Path.Combine(baseDir, "Kassyi.GSharp.Kernels_{{safeAssemblyName}}.gfatbin");
            if (!System.IO.File.Exists(fatbinPath))
            {
            throw new System.InvalidOperationException($"AOT kernel binary was not found at '{fatbinPath}'.");
            }

            byte[] binaryData = System.IO.File.ReadAllBytes(fatbinPath);
            s_hModule = global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.LoadModule(binaryData);
            return s_hModule.Value;
            }
            }

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
            writer.AppendLine($"{warmup.Class.FullyQualifiedName}.WarmupFunctionPointer{warmup.MemberKey}();");
        }

        writer.AppendLine("}}}");

        return writer.ToString();
    }
}
