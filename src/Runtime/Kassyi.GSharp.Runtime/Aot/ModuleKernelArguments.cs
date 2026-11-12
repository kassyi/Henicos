using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

    /// <summary>
    /// Gets the elements of a multidimensional array as one span in row-major order, the order in which a launcher
    /// copies them to the kernel's buffer and back.
    /// </summary>
    /// <remarks>
    /// <b>[WHY]:</b> A rectangular C# array stores its elements contiguously, the last index varying fastest, so the
    /// span is the array's own storage and element <c>[i, j]</c> of an <c>m x n</c> array is at <c>i * n + j</c>, where
    /// a kernel that takes a <c>T*</c> reads it.
    /// </remarks>
    /// <typeparam name="T">The element type of the array.</typeparam>
    /// <param name="array">The array, or <see langword="null"/>.</param>
    /// <returns>The elements of <paramref name="array"/>, or an empty span when it is <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">
    /// The elements of <paramref name="array"/> are not of type <typeparamref name="T"/>.
    /// </exception>
    public static Span<T> Flatten<T>(Array? array) where T : unmanaged
    {
        if (array is null)
        {
            return default;
        }

        if (array.GetType().GetElementType() != typeof(T))
        {
            throw new ArgumentException($"The elements of the array are not of type {typeof(T)}.", nameof(array));
        }

        return MemoryMarshal.CreateSpan(ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(array)),
            array.Length);
    }
}
