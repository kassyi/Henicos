using Kassyi.GSharp.Compiler.Models;
using Kassyi.GSharp.Compiler.Transpiler.Managed;

namespace Kassyi.GSharp.Compiler;

/// <summary>
/// The frame of a device function that may suspend, with the frames of the calls it embeds, and the managed C# forms
/// of the function: its state machine or its managed version.
/// </summary>
internal readonly record struct DeviceFunctionForms(
    string FrameName,
    EquatableArray<string> Frames,
    string ManagedSource,
    ExportedManagedForms Managed)
{
    /// <summary>The frame and the managed forms of the device function of <paramref name="plan"/>.</summary>
    public static DeviceFunctionForms Create(in MethodEmissionPlan plan, MethodDeclarationSyntax bodyDeclaration,
        IMethodSymbol method, Compilation compilation)
    {
        var frameName = "";
        var frameDependencies = ImmutableArray.CreateBuilder<string>();
        var managedSource = "";
        var suspends = plan.Kind == MethodEmissionKind.SuspendingDeviceFunction;
        if (suspends && bodyDeclaration.GetBodyNode() is { } body)
        {
            frameName = GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, method);
            foreach (var callee in SuspensionAnalysis.GetSuspendingCallees(body, compilation))
            {
                frameDependencies.Add(
                    GlobalCppNames.GetKernelDerivedName(GSharpInternalNames.SuspendingCall.FrameTypePrefix, callee));
            }

            // Left out when it could not compile (ManagedStateMachineAvailability); the record says so
            if (ManagedStateMachineAvailability.CanEmit(method, compilation))
            {
                managedSource = MethodTranspiler.GenerateManagedDeviceFunctionSource(in plan);
            }
        }
        else if (plan.HasManagedVersion)
        {
            // A function that does not suspend and expands an operator macro (or calls one that does) runs from its
            // managed version on the managed path (#711)
            managedSource = MethodTranspiler.GenerateManagedVersionSource(in plan);
        }

        // A frame narrower than external callers is run from other assemblies through its entries, which have the
        // accessibility of the function (#1884, #1991, #1845)
        var hasStateMachine = suspends && managedSource.Length > 0;
        var privateFrame = hasStateMachine &&
                           ManagedStateMachineAvailability.HasEntries(
                               method,
                               ManagedStateMachineAvailability.GetFrameAccessibility(method, compilation));
        var managed = new ExportedManagedForms(
            StateMachine: hasStateMachine,
            PrivateFrame: privateFrame,
            Version: !suspends && managedSource.Length > 0
        );
        return new DeviceFunctionForms(frameName, new EquatableArray<string>(frameDependencies.ToImmutable()),
            managedSource, managed);
    }
}
