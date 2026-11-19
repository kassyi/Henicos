namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>What the CPU launcher knows about the kernel it calls for each thread (<see cref="HostLauncherTranspiler.KernelStep"/>).</summary>
[Flags]
internal enum KernelStepTraits
{
    None = 0,

    /// <summary>The kernel takes the context of its thread (it has a state machine, or runs on its own stack).</summary>
    HasContext = 1,

    /// <summary>The kernel returns a value that the launcher stores into the result array.</summary>
    HasAutoResult = 2,

    /// <summary>A cooperative kernel with a state machine: it runs in grid phases.</summary>
    Cooperative = 4,

    /// <summary>The kernel reaches a warp shuffle, so the threads of a warp run in rounds.</summary>
    ReachesWarpShuffle = 8,

    /// <summary>Each GPU thread runs on a native stack of its own (<c>GSharpNativeBarrierModel=Stack</c>).</summary>
    UsesStack = 16
}
