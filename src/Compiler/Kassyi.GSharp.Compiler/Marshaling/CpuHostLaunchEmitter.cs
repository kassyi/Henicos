using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>Emits the CPU host launch members of a kernel wrapper class: delegate, managed fallback and native call.</summary>
internal static class CpuHostLaunchEmitter
{
    private const string PinnedAutoResult = "p_" + GSharpInternalNames.ReturnValue.Buffer;

    // The managed launcher's context slot for the thread currently being stepped.
    private const string LinearContext =
        $"{GSharpInternalNames.Launcher.Contexts}[{GSharpInternalNames.Launcher.LinearThreadIndex}]";

    /// <summary>Emits the managed source, the delegate declaration and the launch wrapper of one kernel.</summary>
    public static void WriteKernelMembers(SourceWriter writer, IMethodSymbol method, string managedCSharpSource)
    {
        writer.AppendLine(managedCSharpSource);
        WriteCpuDelegateDeclaration(writer, method);
        WriteLaunchMethodCpu(writer, method);
    }

    private static void WriteLaunchMethodCpu(SourceWriter writer, IMethodSymbol method)
    {
        writer.AppendLine("#pragma warning disable CS8500");
        WrapperEmitter.WriteLaunchMethodSignature(writer, method, false);
        using (writer.Scope())
        {
            WrapperEmitter.WriteOutParameterDefaults(writer, method);
            WrapperEmitter.WriteArrayArgumentChecks(writer, method);
            WrapperEmitter.WriteAutoResultAllocation(writer, method);
            WriteManagedExecutionFallback(writer, method);
            WriteCpuDelegateGetOrCompile(writer, method);
            WriteCpuFixedBlocksAndInvoke(writer, method);
        }

        writer.AppendLine("#pragma warning restore CS8500");
    }

    private static void WriteCpuDelegateDeclaration(SourceWriter writer, IMethodSymbol method)
    {
        // Generate Delegate
        writer.AppendLine(
            "[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]");
        writer.Append("private unsafe delegate void ");
        writer.Append(KernelHostNames.GetMemberKey(method));
        writer.Append("_Delegate");
        using (writer.Parentheses())
        {
            writer.Append(
                "uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY, uint blockDimZ");
            foreach (var p in method.Parameters)
            {
                if (p.IsConstantParameter())
                {
                    continue;
                }

                writer.Append(", ");

                if (KernelParameterShape.Classify(p).IsArray)
                {
                    writer.Append("global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy ");
                    writer.Append(p.Name.ToCSharpIdentifier());
                }
                else
                {
                    writer.AppendFullyQualifiedCsharpName(p.Type);
                    if (p.RefKind is RefKind.Ref or RefKind.Out)
                    {
                        writer.Append("*");
                    }

                    writer.Append(' ');
                    writer.Append(p.Name.ToCSharpIdentifier());
                }
            }

            if (method.ReturnType.SpecialType != SpecialType.System_Void)
            {
                writer.Append(", ");
                writer.AppendFullyQualifiedCsharpName(method.ReturnType);
                writer.Append($"* {GSharpInternalNames.ReturnValue.Buffer}");
            }
        }

        writer.AppendLine(";");
        writer.AppendLine();
    }

    private static void WriteManagedExecutionFallback(SourceWriter writer, IMethodSymbol method)
    {
        var safeAssemblyName = AotHostNames.ToSafeIdentifier(method.ContainingAssembly.Name);
        var contextName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.ContextPrefix, method);
        var stateMachineName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.StateMachinePrefix, method);

        writer.AppendLine(
            "var archSuffix = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==" +
            " System.Runtime.InteropServices.Architecture.Arm64 ? \"_arm64\" : \"_x64\";");
        writer.AppendLine(
            $"var dllName = $\"Kassyi.GSharp.Kernels_{safeAssemblyName}{{archSuffix}}.dll\";");
        writer.AppendLine(
            "var dllPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, dllName);");

        writer.AppendLine(
            "if (global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ShouldUseManagedExecution(dllPath))");
        using (writer.Scope())
        {
            writer.AppendLine($"int {GSharpInternalNames.Launcher.TotalBlocks} = (int)(gridDimX * gridDimY * gridDimZ);");
            writer.AppendLine($"int {GSharpInternalNames.Launcher.ThreadsPerBlock} = (int)(blockDimX * blockDimY * blockDimZ);");
            writer.AppendLine(
                $"var {GSharpInternalNames.Launcher.Contexts} = new {contextName}[{GSharpInternalNames.Launcher.TotalBlocks} * {GSharpInternalNames.Launcher.ThreadsPerBlock}];");
            writer.AppendLine($"for (int i = 0; i < {GSharpInternalNames.Launcher.Contexts}.Length; i++) {{");
            writer.AppendLine($"  {GSharpInternalNames.Launcher.Contexts}[i] = new {contextName}();");
            writer.AppendLine("}");

            writer.AppendLine(
                $"global::Kassyi.GSharp.Core.ManagedGpuContext.InitializeGrid({GSharpInternalNames.Launcher.TotalBlocks});");

            writer.AppendLine("bool anyGridActive = true;");
            writer.AppendLine("while (anyGridActive)");
            using (writer.Scope())
            {
                writer.AppendLine("anyGridActive = false;");
                writer.AppendLine(
                    $"for (int {GSharpInternalNames.Launcher.BlockLoopIndex} = 0; {GSharpInternalNames.Launcher.BlockLoopIndex} < {GSharpInternalNames.Launcher.TotalBlocks}; {GSharpInternalNames.Launcher.BlockLoopIndex}++)");
                using (writer.Scope())
                {
                    writer.AppendLine($"int {GSharpInternalNames.Launcher.BlockX} = {GSharpInternalNames.Launcher.BlockLoopIndex} % (int)gridDimX;");
                    writer.AppendLine($"int {GSharpInternalNames.Launcher.BlockY} = ({GSharpInternalNames.Launcher.BlockLoopIndex} / (int)gridDimX) % (int)gridDimY;");
                    writer.AppendLine($"int {GSharpInternalNames.Launcher.BlockZ} = {GSharpInternalNames.Launcher.BlockLoopIndex} / (int)(gridDimX * gridDimY);");

                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.IsActive = true;");
                    writer.AppendLine("try");
                    using (writer.Scope())
                    {
                        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxX = {GSharpInternalNames.Launcher.BlockX};");
                        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxY = {GSharpInternalNames.Launcher.BlockY};");
                        writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.BlockIdxZ = {GSharpInternalNames.Launcher.BlockZ};");
                        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimX = (int)blockDimX;");
                        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimY = (int)blockDimY;");
                        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.BlockDimZ = (int)blockDimZ;");
                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimX = (int)gridDimX;");
                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimY = (int)gridDimY;");
                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.GridDimZ = (int)gridDimZ;");
                    writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.ResetWarpExchange();");

                    writer.AppendLine("bool anyBlockActive = true;");
                    writer.AppendLine("while (anyBlockActive)");
                    using (writer.Scope())
                    {
                        writer.AppendLine("anyBlockActive = false;");
                        writer.AppendLine("bool gridSyncRequested = false;");
                        writer.AppendLine(
                            $"for (int {GSharpInternalNames.Launcher.ThreadLoopIndex} = 0; {GSharpInternalNames.Launcher.ThreadLoopIndex} < {GSharpInternalNames.Launcher.ThreadsPerBlock}; {GSharpInternalNames.Launcher.ThreadLoopIndex}++)");
                        using (writer.Scope())
                        {
                            writer.AppendLine($"int {GSharpInternalNames.Launcher.ThreadX} = {GSharpInternalNames.Launcher.ThreadLoopIndex} % (int)blockDimX;");
                            writer.AppendLine(
                                $"int {GSharpInternalNames.Launcher.ThreadY} = ({GSharpInternalNames.Launcher.ThreadLoopIndex} / (int)blockDimX) % (int)blockDimY;");
                            writer.AppendLine(
                                $"int {GSharpInternalNames.Launcher.ThreadZ} = {GSharpInternalNames.Launcher.ThreadLoopIndex} / (int)(blockDimX * blockDimY);");
                            writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxX = {GSharpInternalNames.Launcher.ThreadX};");
                            writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxY = {GSharpInternalNames.Launcher.ThreadY};");
                            writer.AppendLine($"global::Kassyi.GSharp.Core.ManagedGpuContext.ThreadIdxZ = {GSharpInternalNames.Launcher.ThreadZ};");

                            writer.AppendLine(
                                $"int {GSharpInternalNames.Launcher.LinearThreadIndex} = {GSharpInternalNames.Launcher.BlockLoopIndex} * {GSharpInternalNames.Launcher.ThreadsPerBlock} + {GSharpInternalNames.Launcher.ThreadLoopIndex};");
                            writer.AppendLine($"if ({LinearContext}.{GSharpInternalNames.StateMachine.State} != -1)");
                            using (writer.Scope())
                            {
                                writer.AppendLine("anyGridActive = true;");
                                writer.AppendLine("anyBlockActive = true;");
                                writer.AppendLine($"{LinearContext}.{GSharpInternalNames.StateMachine.YieldedForGridSync} = false;");

                                writer.Append($"{stateMachineName}(");
                                bool isFirst = true;
                                foreach (var p in method.Parameters)
                                {
                                    if (!isFirst)
                                    {
                                        writer.Append(", ");
                                    }

                                    isFirst = false;
                                    if (p.RefKind == RefKind.Ref)
                                    {
                                        writer.Append("ref ");
                                    }
                                    else if (p.RefKind == RefKind.Out)
                                    {
                                        writer.Append("out ");
                                    }

                                    writer.Append(p.Name.ToCSharpIdentifier());
                                }

                                if (!isFirst)
                                {
                                    writer.Append(", ");
                                }

                                writer.Append(LinearContext);
                                if (method.ReturnType.SpecialType != SpecialType.System_Void)
                                {
                                    writer.Append($", ref {GSharpInternalNames.ReturnValue.Buffer}[{GSharpInternalNames.Launcher.LinearThreadIndex}]");
                                }

                                writer.AppendLine(");");

                                writer.AppendLine($"if ({LinearContext}.{GSharpInternalNames.StateMachine.YieldedForGridSync})");
                                using (writer.Scope())
                                {
                                    writer.AppendLine("gridSyncRequested = true;");
                                }
                            }
                        }

                        writer.AppendLine("if (gridSyncRequested)");
                        using (writer.Scope())
                        {
                            writer.AppendLine("break;");
                        }
                    }

                    }
                    writer.AppendLine("finally");
                    using (writer.Scope())
                    {
                        writer.AppendLine("global::Kassyi.GSharp.Core.ManagedGpuContext.IsActive = false;");
                    }
                }
            }

            WrapperEmitter.WriteAutoResultReturn(writer, method);
            if (method.ReturnType.SpecialType == SpecialType.System_Void)
            {
                writer.AppendLine("return;");
            }
        }
    }

    private static void WriteCpuDelegateGetOrCompile(SourceWriter writer, IMethodSymbol method)
    {
        var exportName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.Kernel.LaunchExportPrefix, method);

        writer.AppendLine(
            $"var hfunc = global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.LoadAotKernel<{KernelHostNames.GetMemberKey(method)}_Delegate>(\"{exportName}\", dllPath);");
    }

    private static void WriteCpuFixedBlocksAndInvoke(SourceWriter writer, IMethodSymbol method)
    {
        var hasFixed = false;

        foreach (var p in method.Parameters)
        {
            var shape = KernelParameterShape.Classify(p);
            if (shape is { IsArray: true, ElementType: { } elementType })
            {
                writer.Append("fixed (");
                writer.AppendFullyQualifiedCsharpName(elementType);
                writer.AppendLine($"* p_{p.Name} = {shape.SpanExpression})");
                hasFixed = true;
            }
            else if (shape.IsByRef)
            {
                writer.Append("fixed (");
                writer.AppendFullyQualifiedCsharpName(p.Type);
                writer.AppendLine($"* p_{p.Name} = &{p.Name.ToCSharpIdentifier()})");
                hasFixed = true;
            }
        }

        if (method.ReturnType.SpecialType != SpecialType.System_Void)
        {
            writer.Append("fixed (");
            writer.AppendFullyQualifiedCsharpName(method.ReturnType);
            writer.AppendLine($"* {PinnedAutoResult} = {GSharpInternalNames.ReturnValue.Buffer})");
            hasFixed = true;
        }

        if (hasFixed)
        {
            using (writer.Scope())
            {
                WriteDelegateInvocation(writer, method);
            }
        }
        else
        {
            WriteDelegateInvocation(writer, method);
        }

        WrapperEmitter.WriteAutoResultReturn(writer, method);
    }

    private static void WriteDelegateInvocation(SourceWriter writer, IMethodSymbol method)
    {
        writer.Append("hfunc");
        using (writer.Parentheses())
        {
            writer.Append("gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ");

            foreach (var p in method.Parameters)
            {
                if (p.IsConstantParameter())
                {
                    continue;
                }

                writer.Append(", ");

                var shape = KernelParameterShape.Classify(p);
                if (shape.IsArray)
                {
                    writer.Append(
                        $"new global::Kassyi.GSharp.Runtime.Interop.GSharpArrayProxy {{ Data = (IntPtr)p_{p.Name}, Length = {shape.LengthExpression} }}");
                }
                else if (shape.IsByRef)
                {
                    writer.Append("p_");
                    writer.Append(p.Name.ToCSharpIdentifier());
                }
                else
                {
                    writer.Append(p.Name.ToCSharpIdentifier());
                }
            }

            if (method.ReturnType.SpecialType != SpecialType.System_Void)
            {
                writer.Append(", ");
                writer.Append(PinnedAutoResult);
            }
        }

        writer.AppendLine(";");
        writer.AppendLine("global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.ThrowIfNativeError(dllPath);");
    }
}
