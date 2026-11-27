using Kassyi.GSharp.Compiler.Models;

namespace Kassyi.GSharp.Compiler.Analysis;

/// <summary>
/// Shared decision for whether a kernel or suspending device function runs on stacks on both CPU paths under
/// <see cref="CpuBarrierModels"/>, and the diagnostic reason text when it stays on the state machine.
/// </summary>
internal static class StackOnlyExecution
{
    public const string DefaultRefStructReason = "This is restricted by the C# state machine model.";

    public const string DefaultLocalPointerReason = "because the CPU emulator rewinds the stack frame.";

    public static readonly StateMachineReason DefaultStateMachine =
        new(CpuPath.Native, "StateMachineModel", "");

    /// <summary>
    /// Why <paramref name="method"/> keeps (or may keep) a state machine on a CPU path under <paramref name="models"/>,
    /// or <see langword="null"/> when it never runs as one.
    /// </summary>
    public static StateMachineReason? FindStateMachineReason(
        IMethodSymbol method,
        MethodDeclarationSyntax declaration,
        SemanticModel model,
        CpuBarrierModels models)
    {
        if (!models.BothOnStacks)
        {
            return DefaultStateMachine;
        }

        if (!models.BuildsCpuPaths)
        {
            return null;
        }

        if (IsKernel(method))
        {
            return StateMachineReason.ForNative(NativeStackEligibility.Classify(method, declaration, model)) ??
                   ManagedStackEligibility.FindKernelReason(method, model.Compilation);
        }

        return StateMachineReason.ForNative(NativeStackEligibility.ClassifyDeviceFunction(declaration, model)) ??
               ManagedStackEligibility.FindFunctionReason(method, model.Compilation);
    }

    public static string FormatRefStructReason(
        IMethodSymbol method,
        StateMachineReason reason,
        CpuBarrierModels models)
    {
        if (!models.BothOnStacks || reason == DefaultStateMachine)
        {
            return DefaultRefStructReason;
        }

        var kind = IsKernel(method) ? "kernel" : "device function";
        return $"The {kind} '{KernelHostNames.GetMessageName(method)}' runs on the CPU state machine: {reason.Sentence}.";
    }

    public static string FormatLocalPointerReason(
        IMethodSymbol method,
        StateMachineReason reason,
        CpuBarrierModels models)
    {
        if (!models.BothOnStacks || reason == DefaultStateMachine)
        {
            return DefaultLocalPointerReason;
        }

        var kind = IsKernel(method) ? "kernel" : "device function";
        return $"because the {kind} '{KernelHostNames.GetMessageName(method)}' runs on the CPU state machine: {reason.Sentence}.";
    }

    public static string FormatSafetyNetRefStructReason(
        string kernelMessageName,
        string pathLabel,
        string whySentence) =>
        $"The kernel '{kernelMessageName}' runs on the CPU state machine: {pathLabel} path, {whySentence}.";

    public static string FormatSafetyNetLocalPointerReason(
        string kernelMessageName,
        string pathLabel,
        string whySentence) =>
        $"because the kernel '{kernelMessageName}' runs on the CPU state machine: {pathLabel} path, {whySentence}.";

    private static bool IsKernel(IMethodSymbol method) =>
        method.HasAttribute(KnownAttributes.GpuKernel) ||
        method.PartialDefinitionPart?.HasAttribute(KnownAttributes.GpuKernel) == true ||
        method.PartialImplementationPart?.HasAttribute(KnownAttributes.GpuKernel) == true;
}
