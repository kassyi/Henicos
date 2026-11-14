namespace Kassyi.GSharp.Compiler.Models;

/// <summary>What the spill warning (GS0001) of one kernel covers in the native module.</summary>
/// <param name="KernelName">The C++ name of the kernel's entry point (<see cref="GlobalCppNames.GetKernelName"/>).</param>
/// <param name="SuppressesSpill">Whether the kernel is under <c>#pragma warning disable GS0001</c>.</param>
/// <param name="ReachedFunctions">The C++ names of the device functions the kernel reaches (<see cref="DeviceFunctionReach"/>).</param>
internal readonly record struct KernelSpillScope(
    string KernelName,
    bool SuppressesSpill,
    EquatableArray<string> ReachedFunctions);
