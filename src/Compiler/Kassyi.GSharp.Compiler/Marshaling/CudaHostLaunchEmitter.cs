using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits the CUDA host launch members of a kernel wrapper class and the per-assembly module context.</summary>
internal static class CudaHostLaunchEmitter
{

    /// <summary>Emits the function pointer field, warmup method and launch wrappers of one kernel.</summary>
    public static void WriteKernelMembers(SourceWriter writer, in KernelEmission kernel,
        LauncherAccessibilities accessibilities, string moduleName, bool isCheckedBuild)
    {
        WriteFunctionField(writer, kernel, moduleName);
        WrapperEmitter.WriteLayoutCheckedField(writer, kernel.Host.CudaLayoutGuards, kernel.MemberKey);
        WriteWarmupFunctionPointerMethod(writer, kernel);
        WriteLaunchMethod(writer, kernel, false, accessibilities.Launcher, isCheckedBuild);
        WriteLaunchMethod(writer, kernel, true, accessibilities.Pointer, isCheckedBuild);
        if (kernel.IsOverloaded)
        {
            TypedPointerLaunchEmitter.Write(writer, kernel, accessibilities.TypedPointer);
        }
    }

    private static void WriteLaunchMethod(SourceWriter writer, in KernelEmission kernel, bool isDevicePtr,
        Accessibility accessibility, bool isCheckedBuild)
    {
        using var suppression = writer.WarningSuppressed(ManagedPointerWarning.Id, ManagedPointerWarning.Description);
        WrapperEmitter.WriteLaunchMethodSignature(writer, kernel, isDevicePtr, accessibility);
        using (writer.Scope())
        {
            if (!isDevicePtr)
            {
                WrapperEmitter.WriteLayoutGuards(writer, kernel.Host.CudaLayoutGuards, kernel.MemberKey);
                WrapperEmitter.WriteOutParametersAssigned(writer, kernel.Host);
            }
            else
            {
                WrapperEmitter.WritePointerArrayArgumentChecks(writer, kernel.Host);
            }

            LaunchLimitEmitter.WriteCudaCheck(writer, kernel.Host);
            AutoResultEmitter.WriteAllocation(writer, kernel.Host);
            CSharpExecutionEmitter.WriteBranch(writer, CSharpExecutionPlan.Create(kernel.Host, isDevicePtr));

            writer.AppendLine(
                $"using ({HostTypeNames.CudaContextScope} {GSharpInternalNames.Launcher.CudaContextScope} = new())");
            using (writer.Scope())
            {
                WriteCudaKernelCompilationCall(writer, kernel);
                var sharing = ArraySharingPlan.ForCuda(kernel.Host, isDevicePtr);
                var readsOwnRecord = KernelLaunchEmitter.ReadsOwnRecord(isDevicePtr, isCheckedBuild);
                KernelLaunchEmitter.WriteVariablesInitialization(writer, kernel.Host, sharing, isDevicePtr,
                    readsOwnRecord);

                writer.AppendLine("try");
                using (writer.Scope())
                {
                    KernelLaunchEmitter.WriteMarshalAndLaunchKernel(writer, kernel.Host, sharing, isDevicePtr, isCheckedBuild);
                }

                writer.AppendLine("finally");
                using (writer.Scope())
                {
                    KernelLaunchEmitter.WriteCleanup(writer, kernel.Host, sharing, isDevicePtr, readsOwnRecord);
                }
            }

            AutoResultEmitter.WriteReturn(writer, kernel.Host);
        }
    }

    // private static readonly CudaKernelFunction <field> = new CudaKernelFunction("<kernel>", <context>.GetFatbin);
    private static void WriteFunctionField(SourceWriter writer, in KernelEmission kernel, string moduleName)
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

    /// <summary>
    /// Emits the warmup relays (<see cref="WarmupRelayPlan"/>) that go into the outer declaration
    /// <paramref name="outerIndex"/> of a kernel class.
    /// </summary>
    public static void WriteWarmupRelays(SourceWriter writer, EquatableArray<WarmupRelay> relays, int outerIndex)
    {
        foreach (var relay in relays)
        {
            if (relay.OuterIndex != outerIndex)
            {
                continue;
            }

            // internal static void <relay>() => <target type>.<target method>();
            writer.AppendGeneratedCodeAttributes();
            writer.AppendLine($"internal static void {relay.Name}() => {relay.TargetType}.{relay.TargetMethod}();");
        }
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

    public static string GenerateModuleContextClass(string moduleName,
        EquatableArray<KernelWarmup> warmups)
    {
        var fatbinLiteral = SymbolDisplay.FormatLiteral(AotModuleNames.GetArtifactFileStem(moduleName) + ".gfatbin",
            quote: true);
        var moduleContextName = AotModuleNames.GetModuleContextName(moduleName);

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
            byte[]? fatbin = global::System.Threading.Volatile.Read(ref s_fatbin);
            if (fatbin is not null) return fatbin;
            string baseDir = global::System.IO.Path.GetDirectoryName(typeof({{moduleContextName}}).Assembly.Location)
            ?? global::System.AppContext.BaseDirectory;
            string fatbinPath = global::System.IO.Path.Combine(baseDir, {{fatbinLiteral}});
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
            writer.AppendLine($"{warmup.Type}.{warmup.Method}();");
        }

        writer.AppendLine("}}}");

        return writer.ToString();
    }
}
