using Kassyi.GSharp.Compiler.Aot;
using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// Translates a kernel or a suspending device function into its native stack form, for the assembly's own native
/// module (<see cref="Models.GeneratorConfig.RunsOnNativeStacks"/>) and for the device code export records of
/// suspending device functions (<see cref="Models.GeneratorConfig.ExportDeviceCode"/>).
/// </summary>
internal static class NativeStackTranslator
{
    /// <summary>
    /// The C++ text of a kernel: its native stack form when the build runs on native stacks and the kernel can,
    /// otherwise the state machine form (<paramref name="cppSource"/>). An internal error is stated in the C++ text,
    /// as for the other dialects.
    /// </summary>
    public static KernelCppText ForKernel(NativeStackContext context, in MethodEmissionPlan plan, string cppSource,
        HashSet<string> cppIncludes, ErrorSiteTable errorSites)
    {
        var stateMachine = new KernelCppText(cppSource, GpuKernelGenerator.ToIncludes(cppIncludes),
            errorSites.Entries.AsEquatableArray());
        if (!context.OnStacks || context.TargetSymbol is not IMethodSymbol methodSymbol)
        {
            return stateMachine;
        }

        var obstacle = NativeStackEligibility.Classify(methodSymbol, plan.Declaration, plan.SemanticModel);
        if (obstacle == NativeStackObstacle.StackBodyLowering &&
            NativeStackEligibility.StackBodyFailure(methodSymbol, plan.Declaration, plan.SemanticModel) is { } reason)
        {
            return stateMachine with
            {
                Source = StackBodyFailureComment(reason) + cppSource,
                NativeFallback = CreateFallback(methodSymbol, obstacle)
            };
        }

        if (obstacle != NativeStackObstacle.None)
        {
            return stateMachine with { NativeFallback = CreateFallback(methodSymbol, obstacle) };
        }

        try
        {
            var stackFailures = new FailureCollector();
            var stackErrorSites = new ErrorSiteTable();
            var stackSource = stackFailures.Apply(MethodTranspiler.GenerateCudaSource(in plan,
                stackFailures.Fail, new StackCppDialect(), out var requiredIncludesStack,
                new StructSourceCollector(), stackErrorSites));
            return stackSource.Length > 0
                ? new KernelCppText(stackSource, GpuKernelGenerator.ToIncludes(requiredIncludesStack),
                    ErrorSiteTable.MergeSites(stateMachine.ErrorSites, stackErrorSites.Entries.AsEquatableArray()),
                    RunsOnStack: true)
                : stateMachine;
        }
        catch (Exception ex)
        {
            return new KernelCppText(InternalError(ex), default, stateMachine.ErrorSites);
        }
    }

    /// <summary>
    /// Translates the native stack form of a suspending device function once, whenever the build runs on native stacks
    /// or exports its device code, independent of the target dialect.
    /// </summary>
    public static DeviceStackForm TranslateDeviceStackForm(NativeStackContext context, in MethodEmissionPlan plan,
        MethodDeclarationSyntax methodDeclaration)
    {
        if (plan.Kind != MethodEmissionKind.SuspendingDeviceFunction ||
            (!context.OnStacks && !context.ExportsDeviceCode))
        {
            return default;
        }

        var obstacle = NativeStackEligibility.ClassifyDeviceFunction(plan.Declaration, plan.SemanticModel);
        if (obstacle != NativeStackObstacle.None)
        {
            var fallback = context.OnStacks && context.TargetSymbol is IMethodSymbol methodSymbol
                ? CreateFallback(methodSymbol, obstacle)
                : default;
            return new DeviceStackForm(
                HasForm: false,
                Signature: "",
                Source: "",
                ModuleBody: "",
                Includes: null,
                Structs: null,
                ErrorSites: default,
                Failed: false,
                NativeFallback: fallback);
        }

        try
        {
            var stackFailures = new FailureCollector();
            var stackDialect = new StackCppDialect();
            var stackThrowSites = new ErrorSiteTable();
            var stackStructs = new StructSourceCollector();
            var signature = MethodTranspiler.GenerateCudaSignature(in plan, stackFailures.Fail, stackDialect);
            var stackSource = MethodTranspiler.GenerateCudaSource(in plan, stackFailures.Fail, stackDialect,
                out var requiredIncludesStack, stackStructs, throwSites: stackThrowSites);
            var moduleBody = stackFailures.Apply(stackSource);
            var failed = stackFailures.HasFailures;
            var hasForm = !failed && stackSource.Length > 0;
            return new DeviceStackForm(
                HasForm: hasForm,
                Signature: signature,
                Source: hasForm ? stackSource : "",
                ModuleBody: moduleBody,
                Includes: requiredIncludesStack,
                Structs: hasForm ? stackStructs : null,
                ErrorSites: stackThrowSites.Entries.AsEquatableArray(),
                Failed: failed);
        }
        catch (InsufficientExecutionStackException)
        {
            var location = methodDeclaration.Identifier.GetLocation();
            var failure = new TranspileFailure(GSharpDiagnostics.MethodTooComplexToCompile, location,
                [context.TargetSymbol.Name]);
            return new DeviceStackForm(
                HasForm: false,
                Signature: "",
                Source: "",
                ModuleBody: NativeModuleAssembler.FormatTranspileFailures([failure.Format()]),
                Includes: null,
                Structs: null,
                ErrorSites: default,
                Failed: true);
        }
        catch (Exception ex)
        {
            return new DeviceStackForm(
                HasForm: false,
                Signature: "",
                Source: "",
                ModuleBody: InternalError(ex),
                Includes: null,
                Structs: null,
                ErrorSites: default,
                Failed: true);
        }
    }

    /// <summary>
    /// The C++ text of a device function: its stack form (<paramref name="stackForm"/>) after the state machine form
    /// (<paramref name="stateMachine"/>) when the build runs on native stacks and the function suspends and can,
    /// otherwise the state machine form alone.
    /// </summary>
    public static DeviceCppText ForDeviceFunction(NativeStackContext context, DeviceCppText stateMachine,
        in DeviceStackForm stackForm)
    {
        if (!context.OnStacks)
        {
            return stateMachine;
        }

        if (stackForm.NativeFallback != default)
        {
            return stateMachine with { NativeFallback = stackForm.NativeFallback };
        }

        if (stackForm.ModuleBody is not { Length: > 0 } body)
        {
            return stateMachine;
        }

        var includes = stateMachine.Includes;
        if (stackForm.Includes is not null)
        {
            includes = new HashSet<string>(includes);
            includes.UnionWith(stackForm.Includes);
        }

        var errorSites = !stackForm.ErrorSites.IsDefaultOrEmpty
            ? ErrorSiteTable.MergeSites(stateMachine.ErrorSites, stackForm.ErrorSites)
            : stateMachine.ErrorSites;

        return new DeviceCppText(
            AppendOnNewLine(stateMachine.Signature, stackForm.Signature),
            AppendOnNewLine(stateMachine.Body, body),
            includes,
            errorSites);
    }

    private static StateMachineFallback CreateFallback(IMethodSymbol method, NativeStackObstacle obstacle) =>
        StateMachineReason.ForNative(obstacle) is { } reason
            ? new StateMachineFallback(KernelHostNames.GetMessageName(method), reason)
            : default;

    private static string InternalError(Exception ex) =>
        $"/*\n{ex}\n*/\n#error \"Internal Compiler Error: Check comments for details\"\n";

    // addition after existing, on a line of its own
    private static string AppendOnNewLine(string existing, string addition)
    {
        if (addition.Length == 0)
        {
            return existing;
        }

        if (existing.Length == 0)
        {
            return addition;
        }

        return existing[existing.Length - 1] == '\n'
            ? existing + addition
            : existing + "\n" + addition;
    }

    // // G#: runs on the state machine under GSharpNativeBarrierModel=Stack, as its stack body could not be lowered:
    // <method>: <failure>
    // The reason of NativeStackObstacle.StackBodyLowering in the build output, as the managed stack model lists its
    // failures (ManagedStackPipeline); one line, without a backslash that would continue the comment
    internal static string StackBodyFailureComment(string reason)
    {
        using var writer = new SourceWriter();
        writer.Append("// G#: runs on the state machine under GSharpNativeBarrierModel=Stack, ");
        writer.Append("as its stack body could not be lowered: ");
        foreach (var c in reason)
        {
            writer.Append(c is '\r' or '\n' or '\\' ? ' ' : c);
        }

        writer.AppendLine();
        return writer.ToString();
    }
}
