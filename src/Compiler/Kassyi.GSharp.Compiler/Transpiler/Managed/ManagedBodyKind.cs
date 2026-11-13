namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>The managed body that <see cref="ManagedExecutionRewriter"/> rewrites.</summary>
internal enum ManagedBodyKind
{
    /// <summary>The lowered body of a kernel's state machine.</summary>
    Kernel,

    /// <summary>The lowered body of the state machine of a device function that may suspend.</summary>
    SuspendingDeviceFunction,

    /// <summary>The user's body of a device function that does not suspend, copied into its managed version (#711).</summary>
    ManagedVersion
}
