using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits the CUDA host launch members of a kernel wrapper class and the per-assembly module context.</summary>
internal static class CudaHostLaunchEmitter
{
    /// <summary>Emits the function pointer field, warmup method and launch wrappers of one kernel.</summary>
    public static void WriteKernelMembers(SourceWriter writer, IMethodSymbol method)
    {
        writer.AppendLine($"private static IntPtr? s_hfunc_{KernelHostNames.GetMemberKey(method)};");
        WriteWarmupFunctionPointerMethod(writer, method);
        WriteLaunchMethod(writer, method, false);
        WriteLaunchMethod(writer, method, true);
        if (KernelHostNames.IsOverloadedKernel(method))
        {
            WriteTypedPointerLaunchMethod(writer, method);
        }
    }

    private static void WriteLaunchMethod(SourceWriter writer, IMethodSymbol method, bool isDevicePtr)
    {
        writer.AppendLine(
            "#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type");
        WrapperEmitter.WriteLaunchMethodSignature(writer, method, isDevicePtr);
        using (writer.Scope())
        {
            if (!isDevicePtr)
            {
                WrapperEmitter.WriteOutParameterDefaults(writer, method);
                WrapperEmitter.WriteArrayArgumentChecks(writer, method);
            }

            WrapperEmitter.WriteAutoResultAllocation(writer, method);

            writer.AppendLine(
                $"using (var {GSharpInternalNames.Launcher.CudaContextScope} = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())");
            using (writer.Scope())
            {
                WriteCudaKernelCompilationCall(writer, method);
                KernelLaunchEmitter.WriteVariablesInitialization(writer, method, isDevicePtr);

                writer.AppendLine("try");
                using (writer.Scope())
                {
                    KernelLaunchEmitter.WriteMarshalAndLaunchKernel(writer, method, isDevicePtr);
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    KernelLaunchEmitter.WriteCleanup(writer, method, isDevicePtr);
                }
            }

            WrapperEmitter.WriteAutoResultReturn(writer, method);
        }

        writer.AppendLine("#pragma warning restore CS8500");
    }

    private const string LaunchOptionArguments =
        "gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, sharedMemBytes, hStream, macros";

    // The public Launch{Name}Ptr of an overloaded kernel: device pointers are typed GpuBuffer<T>, so C# selects the
    // overload, and the wrapper forwards their device pointers to the private IntPtr implementation.
    private static void WriteTypedPointerLaunchMethod(SourceWriter writer, IMethodSymbol method)
    {
        writer.AppendGeneratedCodeAttributes();
        writer.Append("public static unsafe ");
        WrapperEmitter.WriteLaunchReturnType(writer, method);
        writer.Append(KernelHostNames.GetTypedPointerLaunchName(method));
        using (writer.Parentheses())
        {
            foreach (var p in method.Parameters)
            {
                if (KernelHostNames.TryAppendTypedBuffer(writer, p))
                {
                    var isOptional = p.HasExplicitDefaultValue;
                    writer.Append(isOptional ? "? " : " ").Append(p.Name.ToCSharpIdentifier());
                    if (isOptional)
                    {
                        writer.Append(" = null");
                    }
                }
                else
                {
                    WrapperEmitter.GetInteropParameterInfo(p, true).WriteTo(writer, p.Name.ToCSharpIdentifier());
                }

                writer.Append(", ");
            }

            writer.Append(WrapperEmitter.LaunchOptionParameters);
        }

        writer.AppendLine();
        writer.Append("=> ").Append(KernelHostNames.GetPointerLaunchName(method));
        using (writer.Parentheses())
        {
            foreach (var p in method.Parameters)
            {
                writer.Append(p.Name.ToCSharpIdentifier());
                if (KernelHostNames.TryGetBufferElementType(p, out _))
                {
                    writer.Append(p.HasExplicitDefaultValue ? "?.DevicePointer ?? IntPtr.Zero" : ".DevicePointer");
                }

                writer.Append(", ");
            }

            writer.Append(LaunchOptionArguments);
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void WriteWarmupFunctionPointerMethod(SourceWriter writer, IMethodSymbol method)
    {
        var key = KernelHostNames.GetMemberKey(method);
        writer.AppendLine(
            "/// <summary>Preloads the CUDA module and resolves the function pointer for this kernel.</summary>");
        writer.AppendLine($"public static void WarmupFunctionPointer{key}()");
        using (writer.Scope())
        {
            writer.AppendLine($"if (s_hfunc_{key}.HasValue) return;");
            writer.Append("IntPtr hModule = global::Kassyi.GSharp.Generated.");
            writer.Append(AotHostNames.GetModuleContextName(method.ContainingAssembly.Name));
            writer.AppendLine(".GetOrLoadModule();");
            writer.AppendLine(
                $"s_hfunc_{key} = global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.GetFunction(hModule, \"{GlobalCppNames.GetKernelName(method)}\");");
        }

        writer.AppendLine();
    }

    private static void WriteCudaKernelCompilationCall(SourceWriter writer, IMethodSymbol method)
    {
        var key = KernelHostNames.GetMemberKey(method);
        writer.AppendLine($"WarmupFunctionPointer{key}();");
        writer.AppendLine($"IntPtr hfunc = s_hfunc_{key}!.Value;");
        writer.AppendLine();
    }

    public static string GenerateModuleContextClass(string assemblyName,
        IEnumerable<TranspiledKernelResult> kernelMethods)
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

        foreach (var methodResult in kernelMethods)
        {
            var method = methodResult.MethodSymbol;
            if (KernelHostNames.TryFindIndistinguishableOverload(method, out _, out _))
            {
                continue;
            }

            writer.Append("global::");
            writer.AppendNamespace(method.ContainingNamespace);
            if (!method.ContainingNamespace.IsGlobalNamespace)
            {
                writer.Append('.');
            }
            writer.Append(method.ContainingType.Name);
            writer.AppendLine($".WarmupFunctionPointer{KernelHostNames.GetMemberKey(method)}();");
        }

        writer.AppendLine("}}}");

        return writer.ToString();
    }
}
