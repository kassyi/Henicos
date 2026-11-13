namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Kinds of per-parameter local variables synthesized in host kernel launch wrappers.
/// </summary>
internal enum LauncherLocal
{
    /// <summary>The CUDA device pointer local of a parameter, <c>__gsharp_d_{ordinal}</c>.</summary>
    DevicePointer,

    /// <summary>The stackalloc'd <c>GSharpArrayProxy</c> pointer local of a parameter, <c>__gsharp_proxy_{ordinal}</c>.</summary>
    Proxy,

    /// <summary>
    /// The stackalloc'd <c>System_Nullable&lt;GSharpArray&lt;T&gt;&gt;</c> of a nullable array-like parameter that
    /// holds its <see cref="Proxy"/>, <c>__gsharp_nullable_{ordinal}</c>.
    /// </summary>
    NullableProxy,

    /// <summary>The native shadow struct or span local of a parameter, <c>__gsharp_native_{ordinal}</c>.</summary>
    Native,

    /// <summary>The <c>fixed</c> pointer local of a parameter, <c>__gsharp_p_{ordinal}</c>.</summary>
    Pinned,

    /// <summary>The <c>fixed</c> temporary pointer local of a <c>ref</c> scalar parameter, <c>__gsharp_p_temp_{ordinal}</c>.</summary>
    PinnedTemp,

    /// <summary>The <c>fixed</c> copy-back pointer local of a <c>ref</c> / <c>out</c> scalar parameter, <c>__gsharp_ptr_{ordinal}</c>.</summary>
    ScalarPointer,

    /// <summary>The byte size local of a parameter, <c>__gsharp_size_{ordinal}</c>.</summary>
    Size,

    /// <summary>The rented <c>ArrayPool&lt;byte&gt;</c> shadow buffer local of a parameter, <c>__gsharp_shadow_{ordinal}</c>.</summary>
    Shadow,

    /// <summary>The shared shadow buffer local of a parameter on the CPU, <c>__gsharp_shared_shadow_{ordinal}</c>.</summary>
    SharedShadow,

    /// <summary>The list of pinned <c>GCHandle</c> pointers of a parameter on the CPU, <c>__gsharp_allocated_ptrs_{ordinal}</c>.</summary>
    AllocatedPointers,

    /// <summary>The element loop index local of a converted array parameter, <c>__gsharp_index_{ordinal}</c>.</summary>
    ElementIndex,

    /// <summary>The host span local of a converted array parameter, <c>__gsharp_host_{ordinal}</c>.</summary>
    HostSpan,

    /// <summary>The temporary element copy local of a read-only converted array parameter, <c>__gsharp_element_{ordinal}</c>.</summary>
    Element,

    /// <summary>The saved original array reference local of an <c>[In]</c> array parameter on the CPU, <c>__gsharp_source_{ordinal}</c>.</summary>
    CopySource,

    /// <summary>The cloned array buffer local of an <c>[In]</c> array parameter on the CPU, <c>__gsharp_copy_{ordinal}</c>.</summary>
    CopyBuffer,

    /// <summary>The loop index local that copies nested arrays of a parameter on the CPU, <c>__gsharp_copy_index_{ordinal}</c>.</summary>
    CopyIndex,

    /// <summary>
    /// The local of the C# execution that a GPU thread's call reads: the copy of a <c>ref</c> / <c>out</c> argument, or
    /// the pointer to a pinned span, <c>__gsharp_csharp_arg_{ordinal}</c>.
    /// </summary>
    CSharpArgument,

    /// <summary>
    /// The <c>fixed</c> pointer local of a span argument of the C# execution, <c>__gsharp_csharp_pin_{ordinal}</c>.
    /// </summary>
    CSharpPinned,

    /// <summary>The length of a span argument of the C# execution, <c>__gsharp_csharp_len_{ordinal}</c>.</summary>
    CSharpLength,

    /// <summary>
    /// The local of an external module launcher that holds a kernel argument, whose address the launch passes,
    /// <c>__gsharp_karg_{ordinal}</c>.
    /// </summary>
    KernelArgument,

    /// <summary>
    /// The local of an external module launcher that holds an <c>out</c> value until the write-back,
    /// <c>__gsharp_out_{ordinal}</c>.
    /// </summary>
    OutValue,
}
