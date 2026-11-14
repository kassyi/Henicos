namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>How a managed state machine holds and runs the frame of a device function it calls that may
/// suspend.</summary>
internal enum SuspendingCallForm
{
    /// <summary>
    /// A field of the frame's type, passed to the callee's state machine (<c>__gsharp_statemachine_</c>).
    /// </summary>
    Direct,

    /// <summary>
    /// An <see cref="object"/> field, which the callee's entries create, number and run (<c>__gsharp_number_frame_</c>,
    /// <c>__gsharp_enter_</c>): the frame is private to a class that does not contain the caller.
    /// </summary>
    Entry,

    /// <summary>The callee has no frame (<see cref="ManagedStateMachineAvailability.GetCallFailure(IMethodSymbol,
    /// INamedTypeSymbol, Compilation)"/>).</summary>
    Unavailable
}
