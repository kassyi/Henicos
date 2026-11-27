namespace Kassyi.GSharp.Compiler.Backends;

/// <summary>The backend route a lowered IR body belongs to.</summary>
internal enum IrBodyRoute
{
    /// <summary>The CUDA C++ backend.</summary>
    Cuda,

    /// <summary>The native C++ backend running each GPU thread on its own stack.</summary>
    NativeStack,

    /// <summary>The native C++ backend writing plain bodies without a state machine.</summary>
    NativePlain,

    /// <summary>The managed C# backend.</summary>
    Managed
}
