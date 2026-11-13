using System.ComponentModel;

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>
/// The generated <c>CopyArrays_</c> of an element type: replaces the arrays that <paramref name="element"/> holds with
/// the launch's copies of them (<see cref="HostArrayCopies"/>).
/// </summary>
/// <param name="element">The element of a copy that the kernel gets.</param>
/// <param name="copies">The launch's copies.</param>
/// <typeparam name="T">The element type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Never)]
public delegate void HostArrayElementCopy<T>(ref T element, HostArrayCopies copies);
