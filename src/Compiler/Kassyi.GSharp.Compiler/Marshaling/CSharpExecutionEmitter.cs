namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Writes the branch of a launcher that runs the kernel's own C# method under <c>KernelExecution.CSharp</c> of the
/// runtime, from its <see cref="CSharpExecutionPlan"/>: the CPU launcher (<see cref="CpuHostLaunchEmitter"/>) and the
/// CUDA launchers (<see cref="CudaHostLaunchEmitter"/>) write it after their argument and launch checks.
/// </summary>
internal static class CSharpExecutionEmitter
{
    private const string GpuExecutionType = "global::Kassyi.GSharp.Runtime.GpuExecution";
    private const string KernelExecutionType = "global::Kassyi.GSharp.Runtime.KernelExecution";
    private const string RunnerType = "global::Kassyi.GSharp.Runtime.Aot.CSharpKernelRunner";
    private const string ThreadParameter = GSharpInternalNames.Launcher.CSharpThread;
    private const string AutoResult = GSharpInternalNames.ReturnValue.Buffer;
    private const string Result = GSharpInternalNames.Launcher.CSharpResult;
    private const string MemoryMarshalType = "global::System.Runtime.InteropServices.MemoryMarshal";
    private const string LaunchDimensions = "gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ";

    // if (GpuExecution.Current == KernelExecution.CSharp)
    // { <copies>; <pins> { CSharpKernelRunner.Run(<launch>, <call>); } <copies back>; return [__gsharp_auto_result]; }
    // or, when the kernel cannot run as C#:
    // { throw CSharpKernelRunner.CannotRun("<kernel>", "<reason>"); }
    /// <summary>Writes the C# execution branch of <paramref name="plan"/>.</summary>
    public static void WriteBranch(SourceWriter writer, in CSharpExecutionPlan plan)
    {
        writer.AppendLine($"if ({GpuExecutionType}.Current == {KernelExecutionType}.CSharp)");
        using (writer.Scope())
        {
            if (plan.CannotRunReason is { } reason)
            {
                var kernel = SymbolDisplay.FormatLiteral(plan.MessageName, quote: true);
                var because = SymbolDisplay.FormatLiteral(reason, quote: true);
                writer.AppendLine($"throw {RunnerType}.CannotRun({kernel}, {because});");
                return;
            }

            WriteCopies(writer, plan);
            WritePinnedRun(writer, plan);
            WriteCopiesBack(writer, plan);
            writer.AppendLine(plan.ReturnsValue ? $"return {AutoResult};" : "return;");
        }
    }

    // [<return type>[] __gsharp_csharp_result = __gsharp_auto_result;]
    // <type> __gsharp_csharp_arg_<n> = <parameter>;   (value, in, ref)
    // <type> __gsharp_csharp_arg_<n> = default;       (out)
    // [WHY] The lambda captures only locals of the branch: a captured local of the whole launcher (the auto result)
    // would make C# allocate the closure when the launcher starts, on every path
    private static void WriteCopies(SourceWriter writer, in CSharpExecutionPlan plan)
    {
        writer.AppendLineIf(plan.ReturnsValue, $"{plan.ReturnTypeName}[] {Result} = {AutoResult};");
        foreach (var argument in plan.Arguments)
        {
            if (!IsSpan(argument))
            {
                var value = argument.Kind == CSharpArgumentKind.Out ? "default" : argument.Identifier;
                writer.AppendLine($"{argument.TypeName} {argument.Local} = {value};");
            }
        }
    }

    // <parameter> = __gsharp_csharp_arg_<n>;   (ref, out)
    private static void WriteCopiesBack(SourceWriter writer, in CSharpExecutionPlan plan)
    {
        foreach (var argument in plan.Arguments)
        {
            writer.AppendLineIf(argument.Kind is CSharpArgumentKind.Ref or CSharpArgumentKind.Out,
                $"{argument.Identifier} = {argument.Local};");
        }
    }

    // fixed (<element>* __gsharp_csharp_pin_<n> = &ArrayPinning.GetPinnableReference(<span>)) ... {
    //   <element>* __gsharp_csharp_arg_<n> = <pin>; int __gsharp_csharp_len_<n> = <span>.Length; ... <run> }
    // [WHY] A lambda can capture neither a span nor a fixed local; it captures a copy of the pointer, which stays valid
    // while the caller waits in the fixed statement for the run to end. The pinned reference is null only for a span
    // over no array, so a span over an empty array stays one (fixed (T* p = <span>) would make it default)
    private static void WritePinnedRun(SourceWriter writer, in CSharpExecutionPlan plan)
    {
        var pins = 0;
        foreach (var argument in plan.Arguments)
        {
            if (IsSpan(argument))
            {
                writer.Append($"fixed ({argument.TypeName}* {argument.Pinned} = ");
                writer.AppendLine($"&{HostTypeNames.ArrayPinning}.GetPinnableReference({argument.Identifier}))");
                pins++;
            }
        }

        using (pins > 0 ? writer.Scope() : default)
        {
            foreach (var argument in plan.Arguments)
            {
                if (IsSpan(argument))
                {
                    writer.AppendLine($"{argument.TypeName}* {argument.Local} = {argument.Pinned};");
                    writer.AppendLine($"int {argument.Length} = {argument.Identifier}.Length;");
                }
            }

            WriteRun(writer, plan);
        }
    }

    // CSharpKernelRunner.Run(gridDimX, ..., blockDimZ,
    //     __gsharp_csharp_thread => [__gsharp_csharp_result[__gsharp_csharp_thread] = ]<kernel>(<arguments>));
    private static void WriteRun(SourceWriter writer, in CSharpExecutionPlan plan)
    {
        writer.Append($"{RunnerType}.Run({LaunchDimensions}, {ThreadParameter} => ");
        writer.AppendIf(plan.ReturnsValue, $"{Result}[{ThreadParameter}] = ");
        writer.Append(plan.Method);
        using (writer.Parentheses())
        {
            for (var i = 0; i < plan.Arguments.Length; i++)
            {
                writer.AppendIf(i > 0, ", ");
                WriteArgument(writer, plan.Arguments[i]);
            }
        }

        writer.AppendLine(");");
    }

    private static void WriteArgument(SourceWriter writer, in CSharpArgument argument)
    {
        switch (argument.Kind)
        {
            case CSharpArgumentKind.In:
                writer.Append($"in {argument.Local}");
                break;
            case CSharpArgumentKind.Ref:
                writer.Append($"ref {argument.Local}");
                break;
            case CSharpArgumentKind.Out:
                writer.Append($"out {argument.Local}");
                break;
            // [WHY] Not the pointer constructor of Span, which rejects an element type that holds references; the
            // fixed statement keeps the elements in place while the run uses the span
            case CSharpArgumentKind.Span:
                writer.Append($"{MemoryMarshalType}.CreateSpan(ref *{argument.Local}, {argument.Length})");
                break;
            case CSharpArgumentKind.ReadOnlySpan:
                writer.Append($"{MemoryMarshalType}.CreateReadOnlySpan(ref *{argument.Local}, {argument.Length})");
                break;
            default:
                writer.Append(argument.Local);
                break;
        }
    }

    private static bool IsSpan(in CSharpArgument argument) =>
        argument.Kind is CSharpArgumentKind.Span or CSharpArgumentKind.ReadOnlySpan;
}
