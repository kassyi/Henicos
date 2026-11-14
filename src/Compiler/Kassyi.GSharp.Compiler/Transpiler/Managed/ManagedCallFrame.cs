namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The frame that a managed context or frame holds for one call that may suspend
/// (<see cref="ManagedStateMachineAvailability.PlanCallFrames"/>).
/// </summary>
/// <param name="Callee">The device function called.</param>
/// <param name="ThroughEntry">
/// Whether the frame is an <see cref="object"/> run through the callee's entries
/// (<see cref="SuspendingCallForm.Entry"/>) rather than a field of the frame's type.
/// </param>
internal readonly record struct ManagedCallFrame(IMethodSymbol Callee, bool ThroughEntry);
