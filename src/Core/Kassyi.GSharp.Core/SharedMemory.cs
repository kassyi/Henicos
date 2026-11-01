using System.ComponentModel;
using System.Runtime.CompilerServices;
using Kassyi.GSharp.Hashing;

namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents CUDA block-shared memory (<c>__shared__</c>) allocation within a GPU kernel.
/// </summary>
/// <typeparam name="T">The unmanaged element type to store in shared memory.</typeparam>
/// <remarks>
/// <para>This is a compiler intrinsic placeholder intercepted by the GSharp Transpiler during code generation. It cannot be allocated or accessed directly on the host CPU.</para>
/// <para><b>Execution Target:</b> GPU kernel (<c>[GpuKernel]</c>) device code only.</para>
/// </remarks>
public readonly ref struct SharedMemory<T> where T : unmanaged
{
    /// <summary>
    /// Declares static or dynamic shared memory with the specified element capacity.
    /// </summary>
    private readonly Span<T> _span;

    /// <summary>
    /// Declares static or dynamic shared memory with the specified element capacity.
    /// </summary>
    /// <param name="size">The number of elements allocated in shared memory.</param>
    /// <param name="callerFilePath">The file of the declaration, filled in by the compiler. Do not pass it.</param>
    /// <param name="callerLineNumber">The line of the declaration, filled in by the compiler. Do not pass it.</param>
    /// <param name="callerMemberName">The method of the declaration, filled in by the compiler. Do not pass it.</param>
    /// <remarks>
    /// The declaration has one buffer per block, like a CUDA <c>__shared__</c> variable: every call of the declaring
    /// method, from any call site, gets the same buffer. Under managed execution, code that the G# compiler does not
    /// rewrite (a device function that never reaches a barrier) identifies the declaration by its file, line and method
    /// name, the only facts about the caller that C# passes to a constructor. Two declarations of one method, or of
    /// overloads of one name, on the same line therefore share a buffer.
    /// </remarks>
    public SharedMemory(int size, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0,
        [CallerMemberName] string callerMemberName = "")
        : this(size, ComputeSiteKey(callerFilePath, callerLineNumber, callerMemberName))
    {
    }

    /// <summary>
    /// Declares shared memory for the declaration identified by <paramref name="siteKey"/>. Used by code that the G#
    /// compiler generates for managed execution.
    /// </summary>
    /// <param name="size">The number of elements allocated in shared memory.</param>
    /// <param name="siteKey">The key the compiler computed for the declaration.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SharedMemory(int size, ulong siteKey)
    {
        _span = ManagedGpuContext.IsActive
            ? ManagedGpuContext.AllocateSharedMemory<T>(siteKey ^ ManagedGpuContext.CurrentTypeKeySalt, size)
            : default;
    }

    /// <summary>
    /// Declares dynamic shared memory (a count that is not a C# constant) for the declaration identified by
    /// <paramref name="siteKey"/>: a region of the launch's <c>sharedMemBytes</c>, placed with the CUDA rules. Used by
    /// code that the G# compiler generates for managed execution.
    /// </summary>
    /// <param name="size">The number of elements allocated in shared memory.</param>
    /// <param name="siteKey">The key the compiler computed for the declaration.</param>
    /// <param name="dynamicPlace">The declaration in the C# source, for the error messages.</param>
    /// <exception cref="ArgumentOutOfRangeException">The regions do not fit in the launch's <c>sharedMemBytes</c>.</exception>
    /// <exception cref="InvalidOperationException">The region grows after a later region was placed.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public SharedMemory(int size, ulong siteKey, string dynamicPlace)
    {
        _span = ManagedGpuContext.IsActive
            ? ManagedGpuContext.AllocateDynamicSharedMemory<T>(
                siteKey ^ ManagedGpuContext.CurrentTypeKeySalt, size, dynamicPlace)
            : default;
    }

    private SharedMemory(Span<T> span)
    {
        _span = span;
    }

    /// <summary>
    /// Finds the region of a dynamic declaration that the block has already placed, or none when the declaration was not
    /// reached yet; places nothing. Used by the redeclaration at the start of each resume of a state machine, so that the
    /// declaration itself places the region where the kernel reaches it. It takes no count: the count may be a local that
    /// the resume has not declared yet, and the region keeps the size it was placed with.
    /// </summary>
    /// <param name="siteKey">The key the compiler computed for the declaration.</param>
    /// <returns>The declaration's region, or an empty one.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static SharedMemory<T> FindDynamic(ulong siteKey) =>
        new(ManagedGpuContext.IsActive
            ? ManagedGpuContext.FindDynamicSharedMemory<T>(siteKey ^ ManagedGpuContext.CurrentTypeKeySalt)
            : default);

    /// <summary>
    /// Gets a reference to the element at the specified zero-based index in shared memory.
    /// </summary>
    /// <param name="index">The zero-based element offset.</param>
    /// <returns>A reference to the element at the specified index.</returns>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public ref T this[int index]
    {
        get
        {
            if (!ManagedGpuContext.IsActive)
            {
                GpuOnlyException.Throw();
            }

            return ref _span[index];
        }
    }

    // FNV-1a 64-bit over the path, the line and the method name; the compiler's keys share the same 64-bit space
    private static ulong ComputeSiteKey(string filePath, int lineNumber, string memberName)
    {
        var hash = new Fnv1A64();
        hash.Append(filePath);
        hash.Append(unchecked((uint)lineNumber));
        hash.Append(memberName);
        return hash.Value;
    }
}
