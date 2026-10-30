namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>How a launcher finds the parameters of one launch that hold the same array.</summary>
internal enum ArraySharingMode
{
    /// <summary>
    /// No two holders of one array are possible (at most one <c>T[]</c> argument and no struct or element that holds
    /// arrays), or the launcher copies nothing (the <c>IntPtr</c> wrapper).
    /// </summary>
    None,

    /// <summary>
    /// Only <c>T[]</c> arguments can hold one array: the launcher compares each with the holders before it
    /// (<see cref="ArraySharingPlan.ReferenceEqualsMethod"/>), with no allocation. The CPU launcher always works this way.
    /// </summary>
    Pairwise,

    /// <summary>
    /// A struct argument or a converted element holds arrays: on CUDA every holder looks its array up in the launch's
    /// <c>DeviceArrayRegions</c>, which the generated <c>Marshal_</c> methods also use.
    /// </summary>
    Table
}

/// <summary>
/// Which parameters of a kernel may hold one array, in which order a launcher marshals them, and which holder copies the
/// array in, copies it back or gets a copy of it. The CUDA launcher (<see cref="KernelLaunchEmitter"/>) and the CPU
/// launcher (<see cref="CpuHostLaunchEmitter"/>, <see cref="CpuArrayCopyEmitter"/>) read the same rules from here.
/// </summary>
/// <remarks>
/// In C# two parameters that receive one array are one object, so a write through one is seen through the other (#707,
/// #721). Only a <c>T[]</c> has an identity to compare (<see cref="KernelParameterShape.IsSharableArray"/>); the element
/// type does not matter, since the runtime lets an <c>int[]</c> be passed as a <c>uint[]</c>. The plan keeps the
/// parameters and the mode only; each query walks the parameters, so building a plan allocates nothing.
/// </remarks>
internal readonly struct ArraySharingPlan
{
    /// <summary>The method that the launchers compare two holders with.</summary>
    internal const string ReferenceEqualsMethod = "global::System.Object.ReferenceEquals";

    private readonly EquatableArray<KernelParameterShape> _parameters;
    private readonly bool _isCuda;

    private ArraySharingPlan(EquatableArray<KernelParameterShape> parameters, bool isCuda, ArraySharingMode mode)
    {
        _parameters = parameters;
        _isCuda = isCuda;
        Mode = mode;
    }

    /// <summary>How the launcher finds the holders of one array.</summary>
    public ArraySharingMode Mode { get; }

    /// <summary>The parameters of the kernel, by index.</summary>
    public EquatableArray<KernelParameterShape> Parameters => _parameters;

    /// <summary>Whether the CUDA launcher declares a <c>DeviceArrayRegions</c> for the launch.</summary>
    public bool UsesTable => Mode == ArraySharingMode.Table;

    /// <summary>The plan of the CUDA launcher; <paramref name="isDevicePtr"/> is the <c>IntPtr</c> wrapper, which copies nothing.</summary>
    public static ArraySharingPlan ForCuda(in KernelHostModel kernel, bool isDevicePtr)
    {
        var parameters = kernel.Parameters;
        if (isDevicePtr)
        {
            return new ArraySharingPlan(parameters, true, ArraySharingMode.None);
        }

        // [WHY] A struct or element whose Marshal_ copies arrays records them in the table; T[] arguments alone are
        // compared pairwise, so that most launchers (vector add, SGEMM) allocate nothing per launch
        if (parameters
            .AsStackEnumerable()
            .Any(static shape => shape.MarshalsCudaArrays))
        {
            return new ArraySharingPlan(parameters, true, ArraySharingMode.Table);
        }

        var sharable = parameters
            .AsStackEnumerable()
            .Count(static shape => shape.IsSharableArray);
        return new ArraySharingPlan(parameters, true, sharable > 1 ? ArraySharingMode.Pairwise : ArraySharingMode.None);
    }

    /// <summary>The plan of the CPU launchers (native and managed), which compare the holders pairwise.</summary>
    public static ArraySharingPlan ForCpu(in KernelHostModel kernel) =>
        new(kernel.Parameters, false, ArraySharingMode.Pairwise);

    /// <summary>Whether parameter <paramref name="index"/> is an array argument that may hold the array of another holder.</summary>
    public bool IsShared(int index) => Mode != ArraySharingMode.None && _parameters[index].IsSharableArray;

    /// <summary>
    /// Whether the launcher copies the array of parameter <paramref name="index"/> in: not an <c>[Out]</c> array, except
    /// one of converted elements with array fields, whose arrays <c>Marshal_</c> still sets up.
    /// </summary>
    public bool CopiesIn(int index)
    {
        var shape = _parameters[index];
        return shape.CopyIn || Transfer(shape).ConvertedElementHasArrayFields;
    }

    /// <summary>Whether the launcher copies the array of parameter <paramref name="index"/> back: not <c>[In]</c>, not a read-only container.</summary>
    public bool CopiesBack(int index)
    {
        var shape = _parameters[index];
        return shape.CopyOut && !shape.IsReadOnlyContainer;
    }

    /// <summary>
    /// Whether parameter <paramref name="index"/> is marshaled after every other parameter: a shared holder that copies
    /// nothing in, so that it finds the region (CUDA) or shadow (CPU) that a holder before it filled.
    /// </summary>
    public bool IsMarshaledLast(int index) => IsShared(index) && !CopiesIn(index);

    /// <summary>
    /// Whether <paramref name="other"/> is a shared holder that the launcher marshals before shared holder
    /// <paramref name="index"/>: the holders whose region <paramref name="index"/> takes when it is the same array.
    /// </summary>
    public bool IsEarlierHolder(int index, int other)
    {
        if (other == index || !IsShared(index) || !IsShared(other))
        {
            return false;
        }

        var last = IsMarshaledLast(index);
        return last == IsMarshaledLast(other) ? other < index : last;
    }

    /// <summary>
    /// Whether shared holder <paramref name="other"/> comes before <paramref name="index"/> in the copy back (parameter
    /// order) and copies back: then it already copied the array back when they are one array.
    /// </summary>
    public bool IsEarlierCopyBack(int index, int other) =>
        other < index && IsShared(index) && IsShared(other) && CopiesBack(other);

    /// <summary>
    /// CPU: whether converted <c>T[]</c> <paramref name="other"/> is marshaled before converted <c>T[]</c>
    /// <paramref name="index"/> of the same type, so that <paramref name="index"/> takes its shadow when they are one array.
    /// </summary>
    public bool IsShadowSource(int index, int other)
    {
        var shape = _parameters[index];
        var source = _parameters[other];
        return shape.IsCpuConvertedArray && source.IsCpuConvertedArray && shape.TypeName == source.TypeName &&
            IsEarlierHolder(index, other);
    }

    /// <summary>CPU: whether some converted <c>T[]</c> is the <see cref="IsShadowSource"/> of parameter <paramref name="index"/>.</summary>
    public bool HasShadowSource(int index)
    {
        for (var k = 0; k < _parameters.Length; k++)
        {
            if (IsShadowSource(index, k))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// CPU: whether the kernel gets a copy of parameter <paramref name="index"/> instead of the caller's array: an
    /// <c>[In]</c> array, and a read-only container whose elements hold arrays, which the device never sends back (#798).
    /// </summary>
    public bool NeedsKernelCopy(int index)
    {
        var shape = _parameters[index];
        return shape.IsArray &&
            (shape.IsReadOnlyContainer ? shape.Cpu.ConvertedElementCopyArraysMethodName is not null : !shape.CopyOut);
    }

    /// <summary>
    /// CPU: whether <paramref name="other"/> is an <c>[In]</c> <c>T[]</c> before <c>[In]</c> <c>T[]</c>
    /// <paramref name="index"/>, whose copy <paramref name="index"/> takes when they are one array.
    /// </summary>
    public bool IsEarlierCopy(int index, int other) =>
        other < index && NeedsKernelCopy(index) && NeedsKernelCopy(other) && IsShared(index) && IsShared(other);

    /// <summary>
    /// CPU: whether a later <c>[In]</c> <c>T[]</c> may take the copy of parameter <paramref name="index"/>, which then
    /// keeps the caller's array as <c>&lt;name&gt;_source</c> to compare with.
    /// </summary>
    public bool KeepsSource(int index)
    {
        if (!NeedsKernelCopy(index))
        {
            return false;
        }

        for (var k = index + 1; k < _parameters.Length; k++)
        {
            if (IsEarlierCopy(k, index))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// CPU: whether <paramref name="other"/> is a <c>T[]</c> that the kernel gets uncopied and that may hold the array of
    /// <c>[In]</c> <c>T[]</c> <paramref name="index"/>, which then gets the caller's array too: on CUDA they share one
    /// region, which <paramref name="other"/> copies back.
    /// </summary>
    public bool IsWritableHolder(int index, int other) =>
        other != index && !NeedsKernelCopy(other) && IsShared(index) && IsShared(other);

    /// <summary>
    /// CPU: whether <paramref name="other"/> is a struct argument, or an array argument the kernel gets uncopied, that may
    /// hold the array of <c>[In]</c> <c>T[]</c> <paramref name="index"/> in a field
    /// (<see cref="KernelParameterShape.CpuHoldsArrayMethodName"/>): on CUDA the field shares the region and copies it back.
    /// </summary>
    public bool IsFieldHolder(int index, int other)
    {
        var holder = _parameters[other];
        return other != index && IsShared(index) && holder.CpuHoldsArrayMethodName is not null &&
            (holder.IsCustomStruct || !NeedsKernelCopy(other));
    }

    /// <summary>The <see cref="IsWritableHolder"/> or <see cref="IsFieldHolder"/> of parameter <paramref name="index"/>.</summary>
    public bool IsUncopiedHolder(int index, int other) => IsWritableHolder(index, other) || IsFieldHolder(index, other);

    private ParameterTransfer Transfer(in KernelParameterShape shape) => _isCuda ? shape.Cuda : shape.Cpu;
}
