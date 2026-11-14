namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Emits the call of the CPU native launcher export, which both CPU launchers make: the delegate of
/// <c>Launch{Kernel}</c> (<see cref="CpuHostLaunchEmitter"/>) and the function pointer of <c>Launch{Kernel}Ptr</c>
/// (<see cref="CpuPointerLaunchEmitter"/>), each argument in the form <see cref="CpuNativeArgumentPlan"/> chose.
/// </summary>
internal static class CpuNativeCallEmitter
{
    private const string FunctionHandle = GSharpInternalNames.Launcher.FunctionHandle;
    private const string DllPath = GSharpInternalNames.Launcher.DllPath;
    private const string GridBlockCount = GSharpInternalNames.Launcher.GridBlockCount;

    // __gsharp_hfunc(<launch dimensions>, __gsharp_launch_block_count, <arguments>[, p___gsharp_auto_result]);
    // CpuKernelLoader.ThrowIfNativeError(__gsharp_dll_path);
    // The call of the delegate of Launch{Kernel} and of the function pointer of Launch{Kernel}Ptr
    internal static void WriteCall(SourceWriter writer, in KernelHostModel kernel,
        in CpuNativeArgumentPlan arguments)
    {
        writer.Append(FunctionHandle);
        using (writer.Parentheses())
        {
            // The native launcher places the dynamic shared memory declarations in sharedMemBytes (#544) and runs the
            // number of blocks that the launch check counted in 64 bits (#805)
            writer.Append("gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, ");
            writer.Append($"sharedMemBytes, {GridBlockCount}");

            var parameters = kernel.Parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                writer.Append(", ");
                WriteArgument(writer, parameters[i], arguments.Arguments[i]);
            }

            writer.AppendIf(arguments.ResultTypeName is not null, $", {AutoResultEmitter.PinnedAutoResult}");
        }

        writer.AppendLine(";");
        writer.AppendLine($"{HostTypeNames.CpuKernelLoader}.ThrowIfNativeError({DllPath});");
    }

    // The argument in the form that CpuNativeArgumentPlan chose
    // [WHY] A switch statement, not a switch expression: each form is written straight into the writer, so no argument
    // string is built (transpiler-emission.md section 1)
    // <group> of the argument is the member test and buffer address of a converted member of a group of overlapping
    // arguments, which the native kernel takes instead of the member's own shadow
    private static void WriteArgument(SourceWriter writer, in KernelParameterShape shape,
        in CpuNativeArgument argument)
    {
        switch (argument.Form)
        {
            case CpuNativeArgumentForm.NullableProxyAddress:
                writer.Append($"&{KernelHostNames.GetLocal(LauncherLocal.NullableProxy, shape)}");
                break;
            case CpuNativeArgumentForm.ArrayProxy:
                WriteArrayProxy(writer, shape, argument.Group);
                break;
            case CpuNativeArgumentForm.ShadowAddress:
                writer.AppendIf(argument.Group is not null,
                    $"{argument.Group?.Member} ? ({shape.AotStructPath}*){argument.Group?.Pointer} : ");
                writer.Append($"&{KernelHostNames.GetLocal(LauncherLocal.Native, shape)}");
                break;
            case CpuNativeArgumentForm.PinnedAddress:
                writer.Append(KernelHostNames.GetLocal(LauncherLocal.Pinned, shape));
                break;
            case CpuNativeArgumentForm.ValueAddress:
                // The parameter of Launch is a value parameter, a fixed variable whose address needs no pinning
                writer.Append($"&{shape.Identifier}");
                break;
            case CpuNativeArgumentForm.ProxyCopy:
                WriteProxyCopy(writer, shape.Identifier);
                break;
            case CpuNativeArgumentForm.PointerCast:
                writer.Append($"({argument.TypeName}){shape.Identifier}");
                break;
            case CpuNativeArgumentForm.BooleanByte:
                writer.Append($"{shape.Identifier} ? (byte)1 : (byte)0");
                break;
            case CpuNativeArgumentForm.CharCode:
                writer.Append($"(ushort){shape.Identifier}");
                break;
            default:
                writer.Append(shape.Identifier);
                break;
        }
    }

    // new GSharpArrayProxy { Data = <proxy>.Data, Length = <proxy>.Length }
    // [WHY] The reserved Flags of the caller's proxy stay behind: every launcher passes them as 0
    internal static void WriteProxyCopy(SourceWriter writer, string proxy) =>
        writer.Append($"new {HostTypeNames.ArrayProxy} {{ Data = {proxy}.Data, Length = {proxy}.Length }}");

    // An argument passed as it is:
    // new GSharpArrayProxy { Data = (IntPtr)__gsharp_p_<ord>, Length = <length> }
    // A converted array:
    // new GSharpArrayProxy { Data = <null test> ? IntPtr.Zero : (IntPtr)__gsharp_p_<ord>, Length = <length> }
    // [WHY] The shadow of a null array is an empty rented array, whose pinned address is not null
    // A converted member of a group: ... : <member> ? <buffer address> : (IntPtr)__gsharp_p_<ord> ...
    internal static void WriteArrayProxy(SourceWriter writer, in KernelParameterShape shape,
        (string Member, string Pointer)? group)
    {
        var pinned = KernelHostNames.GetLocal(LauncherLocal.Pinned, shape);
        writer.Append($"new {HostTypeNames.ArrayProxy} {{ Data = ");
        writer.AppendIf(shape.IsCpuConvertedArray, $"{shape.NullTestExpression} ? {HostTypeNames.IntPtr}.Zero : ");
        writer.AppendIf(group is not null, $"{group?.Member} ? {group?.Pointer} : ");
        writer.Append($"({HostTypeNames.IntPtr}){pinned}, Length = {shape.LengthExpression} }}");
    }
}
