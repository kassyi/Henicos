namespace Kassyi.GSharp.Core;

/// <summary>
/// Marks a kernel parameter, field, or property as read-only for the whole launch. An array, span or other
/// array-like value is emitted with <c>const</c> elements, and any other value with the <c>const</c> qualifier, in the
/// generated C++.
/// </summary>
/// <remarks>
/// <para>
/// On an array the attribute is a promise: no thread writes the array while the kernel runs, through any alias.
/// G# reports a write it can see (GSH0118): an element assignment, <c>++</c>, a <c>ref</c> element, <c>Fill</c> and
/// the like, also through a span or array converted from the array and through a method that writes its parameter.
/// It cannot see the same host array passed to a <c>[GpuReadOnly]</c> parameter and to a writable one, and a write
/// there breaks the promise: the result is undefined. The promise lets later versions read the elements through the
/// read-only data path of the GPU.
/// </para>
/// <para>
/// The attribute does not place anything in constant memory and adds no <c>__restrict__</c> today; it changes the
/// type of the C++ declaration only.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class GpuReadOnlyAttribute : Attribute;
