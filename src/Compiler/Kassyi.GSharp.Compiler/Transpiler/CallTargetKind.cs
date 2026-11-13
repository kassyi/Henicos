namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>What a call in GPU code, or a method group it names, calls on the device.</summary>
internal enum CallTargetKind
{
    /// <summary>
    /// C# removes the call: a partial method without an implementation, or a <c>[Conditional]</c> one.
    /// </summary>
    Removed,

    /// <summary>A call that lowering synthesized, which has no written call to decide.</summary>
    Synthesized,

    /// <summary>Not an ordinary method: a delegate's <c>Invoke</c>, a local function, an accessor.</summary>
    NotOrdinary,

    /// <summary>
    /// A <c>[GpuDeviceFunction]</c> or <c>[GpuKernel]</c> method, of the project or of another assembly.
    /// </summary>
    DeviceFunction,

    /// <summary>
    /// A native function of the dialect: <c>[CudaIntrinsic]</c>, an intrinsic pattern, <c>[CudaImport]</c>, or a row
    /// of the dialect's YAML.
    /// </summary>
    DialectIntrinsic,

    /// <summary>
    /// A <c>[CudaIntrinsic]</c> method whose attribute says the dialect does not implement it (the <c>unsupported</c>
    /// column of the native API YAML): reported as a warning (GSH0054) and written as a runtime failure.
    /// </summary>
    UnsupportedIntrinsic,

    /// <summary>A method that <c>[BclIntrinsicMap]</c> maps.</summary>
    BclIntrinsicMap,

    /// <summary>A <c>[GpuOperatorMacro]</c> method.</summary>
    OperatorMacro,

    /// <summary>
    /// A call that a rule writes before the standard emission: a <c>Gpu</c> macro where its rule applies, StackLinq, a
    /// span, array or <c>MemoryExtensions</c> member, <c>Nullable&lt;T&gt;.GetValueOrDefault</c>.
    /// </summary>
    ClaimedByRule,

    /// <summary>
    /// A method of <c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c> or
    /// <c>ImmutableArray&lt;T&gt;</c> that the array view of device code defines with its C# meaning
    /// (<see cref="Analysis.ArrayViewMethods"/>).
    /// </summary>
    ArrayViewMember,

    /// <summary>
    /// A <c>Gpu</c> macro written where no macro rule writes it: outside a statement of its own, or with a body that
    /// is not a lambda (GSH0003).
    /// </summary>
    MisusedMacro,

    /// <summary>A method of the project without a device definition (GSH0107).</summary>
    Unemitted,

    /// <summary>A method of another assembly that the dialect does not implement (GSH0109).</summary>
    Unmapped,

    /// <summary>
    /// <c>nameof</c> whose value stays a string on the device (GSH0002): a length or a comparison of constant strings
    /// is written as its value before the <c>nameof</c> is reached.
    /// </summary>
    NameofString,

    /// <summary>
    /// An invocation that binds to no method and that lowering did not synthesize (GSH0003): a call through a function
    /// pointer or a call bound at run time.
    /// </summary>
    Unbound,

    /// <summary>
    /// An invocation that C# cannot bind (a name that does not exist, no applicable overload): the C# compiler reports
    /// it, so it is not reported again and only the placeholder is written for it.
    /// </summary>
    NotCSharp
}
