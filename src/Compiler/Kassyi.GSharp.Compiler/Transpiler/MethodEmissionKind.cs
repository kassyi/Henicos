namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>What a method is emitted as.</summary>
internal enum MethodEmissionKind
{
    /// <summary>A kernel entry point.</summary>
    Kernel,

    /// <summary>
    /// A device function that never suspends. The managed path runs the user's method, or its managed version when it
    /// expands an operator macro (<see cref="MethodEmissionPlan.HasManagedVersion"/>).
    /// </summary>
    DeviceFunction,

    /// <summary>
    /// A device function that reaches a barrier and is not recursive. The CPU dialects and the managed output lower it into
    /// a state machine with a frame; the dialects with hardware threads emit it as a plain device function.
    /// </summary>
    SuspendingDeviceFunction,

    /// <summary>
    /// A static <c>[GpuDeviceFunction]</c> returning <c>IEnumerable&lt;T&gt;</c> that uses <c>yield</c>. Expanded inline at
    /// each <c>foreach</c> site in device code; emits no standalone device function. The managed path uses the user's C#
    /// iterator directly, or its managed version as for <see cref="DeviceFunction"/>.
    /// </summary>
    Iterator
}
