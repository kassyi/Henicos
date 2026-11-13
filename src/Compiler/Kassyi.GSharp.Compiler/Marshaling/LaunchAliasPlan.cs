namespace Kassyi.GSharp.Compiler.Marshaling;

/// <summary>
/// Which arguments of a launch the launcher compares by address for overlaps (<c>HostAliasGroups</c> of the runtime),
/// and the copy directions of each: the slices, spans and segments, the <c>ref</c> and <c>out</c> scalars and structs,
/// and every <c>T[]</c> beside them. <see cref="AliasGroupEmitter"/> writes it.
/// </summary>
/// <remarks>
/// In C# two slices of one array, an element passed by <c>ref</c> and its array, or one variable passed to two
/// <c>ref</c> parameters are one memory (#857, #1061). <see cref="ArraySharingPlan"/> compares <c>T[]</c> arguments by
/// identity only, which covers whole arrays; a launch with no other candidate needs no comparison by address and keeps
/// the launcher as it was. Arguments whose elements are converted between the host and the device layout cannot share
/// bytes; they are compared too, so that an overlap throws instead of giving wrong values.
/// </remarks>
internal readonly struct LaunchAliasPlan
{
    // HostAliasGroups traits
    internal const byte CopiesIn = 1;
    internal const byte CopiesBack = 2;
    internal const byte Unsupported = 4;
    internal const byte WholeArray = 8;
    internal const byte SpanView = 16;

    private LaunchAliasPlan(ImmutableArray<int> candidates, ImmutableArray<byte> traits,
        ImmutableArray<AliasRange> ranges)
    {
        Candidates = candidates;
        Traits = traits;
        Ranges = ranges;
    }

    /// <summary>The parameters that the launcher compares, in parameter order; empty when it compares none.</summary>
    public ImmutableArray<int> Candidates { get; }

    /// <summary>The <c>HostAliasGroups</c> traits of each candidate.</summary>
    public ImmutableArray<byte> Traits { get; }

    /// <summary>The pins and ranges of the candidates, in the order of <see cref="Candidates"/>.</summary>
    public ImmutableArray<AliasRange> Ranges { get; }

    /// <summary>Whether the launcher compares any arguments.</summary>
    public bool IsActive => !Candidates.IsDefaultOrEmpty;

    /// <summary>The plan of the CUDA launcher that copies arguments; the <c>IntPtr</c> one copies nothing.</summary>
    public static LaunchAliasPlan ForCuda(in ArraySharingPlan sharing, bool isDevicePtr)
    {
        if (isDevicePtr)
        {
            return default;
        }

        var parameters = sharing.Parameters;
        if (!HasPartialCandidate(parameters))
        {
            return default;
        }

        var candidates = ImmutableArray.CreateBuilder<int>();
        var traits = ImmutableArray.CreateBuilder<byte>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var shape = parameters[i];
            if (!IsCandidate(shape))
            {
                continue;
            }

            var copiesIn = shape.IsArray ? sharing.CopiesIn(i) : shape.RefKind == RefKind.Ref;
            var copiesBack = !shape.IsArray || sharing.CopiesBack(i);
            var converted = shape.IsArray
                ? shape.IsCudaConvertedArray
                : shape.IsCustomStruct && (!shape.Cuda.IsIdenticalStruct || shape.HasArrayFields);
            candidates.Add(i);
            traits.Add(GetTraits(shape, copiesIn, copiesBack, converted, spanView: false));
        }

        return Create(parameters, candidates, traits);
    }

    /// <summary>
    /// The plan of the CPU launcher, which hands the kernel the caller's memory except the copies of <c>[In]</c>
    /// arguments (<see cref="ArraySharingPlan.NeedsKernelCopy"/>): an <c>[In]</c> or read-only argument is copied in
    /// only, any other one is the caller's memory, read and written. Without a copy (an <c>[In]</c> argument or a
    /// converted element) the caller's memory already overlaps as in C#, so the launcher compares nothing.
    /// </summary>
    public static LaunchAliasPlan ForCpu(in ArraySharingPlan sharing)
    {
        var parameters = sharing.Parameters;
        if (!HasPartialCandidate(parameters))
        {
            return default;
        }

        var copies = false;
        var candidates = ImmutableArray.CreateBuilder<int>();
        var traits = ImmutableArray.CreateBuilder<byte>();
        for (var i = 0; i < parameters.Length; i++)
        {
            var shape = parameters[i];
            if (!IsCandidate(shape))
            {
                continue;
            }

            var readOnly = shape.IsArray && (sharing.NeedsKernelCopy(i) || shape.IsReadOnlyContainer);
            var converted = shape.IsArray ? shape.IsCpuConvertedArray : shape.IsCpuConvertedStruct;
            copies |= converted || (shape.IsArray && sharing.NeedsKernelCopy(i));
            var spanView = shape.Container is ArrayContainerKind.Span or ArrayContainerKind.ReadOnlySpan;
            candidates.Add(i);
            traits.Add(GetTraits(shape, copiesIn: true, copiesBack: !readOnly, converted, spanView));
        }

        return copies ? Create(parameters, candidates, traits) : default;
    }

    /// <summary>The candidate number of parameter <paramref name="parameterIndex"/>, or -1.</summary>
    public int IndexOf(int parameterIndex) => IsActive ? Candidates.IndexOf(parameterIndex) : -1;

    /// <summary>
    /// The candidate number of parameter <paramref name="parameterIndex"/> when it can be a member of a group, whose
    /// copy the launcher then takes from the group, or -1 (not a candidate, or one whose overlap throws).
    /// </summary>
    public int MemberIndexOf(int parameterIndex)
    {
        var index = IndexOf(parameterIndex);
        return index >= 0 && (Traits[index] & Unsupported) == 0 ? index : -1;
    }

    /// <summary>
    /// CPU: whether parameter <paramref name="parameterIndex"/> can take the copy of its group: a member that is copied
    /// in only (<c>[In]</c> or read-only), the only kind whose group may be all <c>[In]</c>.
    /// </summary>
    public bool MayTakeGroupCopy(int parameterIndex)
    {
        var index = MemberIndexOf(parameterIndex);
        return index >= 0 && (Traits[index] & (CopiesIn | CopiesBack)) == CopiesIn;
    }

    // [WHY] Without a slice or a by-reference argument, only T[] arguments can share memory, and two of them overlap
    // only when they are one array, which ArraySharingPlan already compares by identity
    private static bool HasPartialCandidate(EquatableArray<KernelParameterShape> parameters) =>
        parameters
            .AsStackEnumerable()
            .Any(static shape => IsCandidate(shape) && !shape.IsSharableArray) &&
        parameters
            .AsStackEnumerable()
            .Count(static shape => IsCandidate(shape)) > 1;

    // An array-like argument passed by value, or a scalar or struct passed by ref / out
    private static bool IsCandidate(in KernelParameterShape shape) =>
        shape.IsArray ? !shape.IsByRef : shape.IsByRef;

    private static byte GetTraits(in KernelParameterShape shape, bool copiesIn, bool copiesBack, bool converted,
        bool spanView)
    {
        var traits = 0;
        traits |= copiesIn ? CopiesIn : 0;
        traits |= copiesBack ? CopiesBack : 0;
        traits |= converted ? Unsupported : 0;
        traits |= shape.IsSharableArray ? WholeArray : 0;
        traits |= spanView ? SpanView : 0;
        return (byte)traits;
    }

    private static LaunchAliasPlan Create(EquatableArray<KernelParameterShape> parameters,
        ImmutableArray<int>.Builder candidates, ImmutableArray<byte>.Builder traits)
    {
        var ranges = ImmutableArray.CreateBuilder<AliasRange>(candidates.Count);
        for (var k = 0; k < candidates.Count; k++)
        {
            ranges.Add(GetRange(parameters[candidates[k]], traits[k]));
        }

        return new LaunchAliasPlan(candidates.ToImmutable(), traits.ToImmutable(), ranges.MoveToImmutable());
    }

    // An array-like argument pins its span; a ref or out value pins the variable
    private static AliasRange GetRange(in KernelParameterShape shape, byte traits)
    {
        var pinned = KernelHostNames.GetLocal(LauncherLocal.AliasPinned, shape);
        return shape.IsArray
            ? AliasRange.ForArray(pinned, shape.SpanExpression, shape.LengthExpression,
                ConvertedArrayEmitter.GetElementTypeName(shape), traits, shape.Name)
            : AliasRange.ForReference(pinned, shape.Identifier, shape.TypeName, traits, shape.Name);
    }
}
