using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Helpers that the generated launchers of external kernel modules (<c>[CudaImport]</c> methods and YAML
/// <c>moduleName</c> bindings) call to build the argument array of <c>cuLaunchKernel</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ModuleKernelArguments
{
    /// <summary>
    /// Gets the address of a launcher local that holds one kernel argument, for the <c>kernelParams</c> array of
    /// <c>cuLaunchKernel</c>.
    /// </summary>
    /// <remarks>
    /// <b>[WHY]:</b> The constraint makes a reference type a compile error (CS8377) in the generated launcher: taking
    /// the address of a local that holds a reference would pass the kernel the host address of the reference. A YAML
    /// binding names its types as text, so its reader cannot tell a class from a struct; the C# compiler can.
    /// </remarks>
    /// <typeparam name="T">The type of the argument.</typeparam>
    /// <param name="value">A local of the launcher, which stays on the stack until the launch returns.</param>
    /// <returns>The address of <paramref name="value"/>.</returns>
    public static unsafe void* AddressOf<T>(ref T value) where T : unmanaged => Unsafe.AsPointer(ref value);
}
