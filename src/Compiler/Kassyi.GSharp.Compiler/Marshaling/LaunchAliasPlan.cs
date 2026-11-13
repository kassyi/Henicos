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
/// the launcher as it was. Arguments whose elements are converted between the host and the device layout carry the
/// number of their element type (<see cref="Conversions"/>), so that a group of them is converted as a whole.
/// </remarks>
internal readonly struct LaunchAliasPlan
{
    // HostAliasGroups traits
    internal const byte CopiesIn = 1;
    internal const byte CopiesBack = 2;
    internal const byte WholeArray = 8;
    internal const byte SpanView = 16;

    private LaunchAliasPlan(ImmutableArray<int> candidates, ImmutableArray<byte> traits,
        ImmutableArray<AliasRange> ranges, ImmutableArray<AliasConversion> conversions)
    {
        Candidates = candidates;
        Traits = traits;
        Ranges = ranges;
        Conversions = conversions;
    }

    /// <summary>The parameters that the launcher compares, in parameter order; empty when it compares none.</summary>
    public ImmutableArray<int> Candidates { get; }

    /// <summary>The <c>HostAliasGroups</c> traits of each candidate.</summary>
    public ImmutableArray<byte> Traits { get; }

    /// <summary>The pins and ranges of the candidates, in the order of <see cref="Candidates"/>.</summary>
    public ImmutableArray<AliasRange> Ranges { get; }

    /// <summary>The converted element types among the candidates, one per type.</summary>
    public ImmutableArray<AliasConversion> Conversions { get; }

    /// <summary>Whether the launcher compares any arguments.</summary>
    public bool IsActive => !Candidates.IsDefaultOrEmpty;

    /// <summary>Whether a candidate's elements are converted, so that its group may be converted as a whole.</summary>
    public bool HasConversions => !Conversions.IsDefaultOrEmpty;

    /// <summary>The plan of the CUDA launcher that copies arguments; the <c>IntPtr</c> one copies nothing.</summary>
    public static LaunchAliasPlan ForCuda(in ArraySharingPlan sharing, bool isDevicePtr)
    {
        var parameters = sharing.Parameters;
        if (isDevicePtr || !HasPartialCandidate(parameters))
        {
            return default;
        }

        var builder = new Builder(parameters, HostDialects.Cuda);
        for (var i = 0; i < parameters.Length; i++)
        {
            var shape = parameters[i];
            if (IsCandidate(shape))
            {
                var copiesIn = shape.IsArray ? sharing.CopiesIn(i) : shape.RefKind != RefKind.Out;
                var copiesBack = shape.IsArray ? sharing.CopiesBack(i) : !shape.IsInReference;
                builder.Add(i, GetTraits(shape, copiesIn, copiesBack, spanView: false));
            }
        }

        return builder.ToPlan();
    }

    /// <summary>
    /// The plan of the CPU launcher, which hands the kernel the caller's memory except the copies of <c>[In]</c>
    /// arguments (<see cref="ArraySharingPlan.NeedsKernelCopy"/>) and the shadows of converted elements: an <c>[In]</c>
    /// or read-only argument is copied in only, any other one is the caller's memory, read and written. Without a copy
    /// or a shadow the caller's memory already overlaps as in C#, so the launcher compares nothing.
    /// </summary>
    public static LaunchAliasPlan ForCpu(in ArraySharingPlan sharing)
    {
        var parameters = sharing.Parameters;
        if (!HasPartialCandidate(parameters))
        {
            return default;
        }

        var copies = false;
        var builder = new Builder(parameters, HostDialects.Cpu);
        for (var i = 0; i < parameters.Length; i++)
        {
            var shape = parameters[i];
            if (!IsCandidate(shape))
            {
                continue;
            }

            var readOnly = shape.IsArray && (sharing.NeedsKernelCopy(i) || shape.IsReadOnlyContainer);
            var spanView = shape.Container is ArrayContainerKind.Span or ArrayContainerKind.ReadOnlySpan;
            copies |= IsConverted(shape, HostDialects.Cpu) || (shape.IsArray && sharing.NeedsKernelCopy(i));
            builder.Add(i, GetTraits(shape, copiesIn: true, copiesBack: !readOnly && !shape.IsInReference, spanView));
        }

        return copies ? builder.ToPlan() : default;
    }

    /// <summary>The candidate number of parameter <paramref name="parameterIndex"/>, or -1.</summary>
    public int MemberIndexOf(int parameterIndex) => IsActive ? Candidates.IndexOf(parameterIndex) : -1;

    /// <summary>Whether parameter <paramref name="parameterIndex"/> is a candidate of converted elements.</summary>
    public bool IsConvertedMember(int parameterIndex) =>
        MemberIndexOf(parameterIndex) is var index and >= 0 && Ranges[index].ConversionKey != 0;

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

    // An array of elements, or a struct by reference, that the launcher of the dialect converts through a shadow
    private static bool IsConverted(in KernelParameterShape shape, ITranspilerDialect dialect) =>
        dialect.IsCuda
            ? shape.IsCudaConvertedArray ||
                (shape.IsCustomStruct && (!shape.Cuda.IsIdenticalStruct || shape.HasArrayFields))
            : shape.IsCpuConvertedArray || shape.IsCpuConvertedStruct;

    private static byte GetTraits(in KernelParameterShape shape, bool copiesIn, bool copiesBack, bool spanView)
    {
        var traits = 0;
        traits |= copiesIn ? CopiesIn : 0;
        traits |= copiesBack ? CopiesBack : 0;
        traits |= shape.IsSharableArray ? WholeArray : 0;
        traits |= spanView ? SpanView : 0;
        return (byte)traits;
    }

    // Collects the candidates with their ranges and numbers the converted element types in the order they appear
    private sealed class Builder(EquatableArray<KernelParameterShape> parameters, ITranspilerDialect dialect)
    {
        private readonly ImmutableArray<int>.Builder _candidates = ImmutableArray.CreateBuilder<int>();
        private readonly ImmutableArray<byte>.Builder _traits = ImmutableArray.CreateBuilder<byte>();
        private readonly ImmutableArray<AliasRange>.Builder _ranges = ImmutableArray.CreateBuilder<AliasRange>();
        private readonly ImmutableArray<AliasConversion>.Builder _conversions =
            ImmutableArray.CreateBuilder<AliasConversion>();

        public void Add(int index, byte traits)
        {
            var shape = parameters[index];
            var element = GetElement(shape);
            var pinned = KernelHostNames.GetLocal(LauncherLocal.AliasPinned, shape);
            _candidates.Add(index);
            _traits.Add(traits);
            _ranges.Add(shape.IsArray
                ? AliasRange.ForArray(pinned, shape.SpanExpression, shape.LengthExpression, element, traits, shape.Name)
                : AliasRange.ForReference(pinned, shape.ReferenceExpression, element, traits, shape.Name));
        }

        public LaunchAliasPlan ToPlan() =>
            new(_candidates.ToImmutable(), _traits.ToImmutable(), _ranges.ToImmutable(), _conversions.ToImmutable());

        private AliasElement GetElement(in KernelParameterShape shape)
        {
            var host = shape.IsArray ? ConvertedArrayEmitter.GetElementTypeName(shape) : shape.TypeName;
            var alignment = dialect.IsCuda ? shape.CudaAliasAlignment : shape.CpuAliasAlignment;
            if (!IsConverted(shape, dialect))
            {
                return AliasElement.Raw(host, alignment);
            }

            var conversion = GetConversion(shape, host);
            return new AliasElement(host, conversion.ShadowType, alignment, conversion.Key);
        }

        // The conversion of the element type of <shape>, numbered on its first use
        private AliasConversion GetConversion(in KernelParameterShape shape, string host)
        {
            foreach (var conversion in _conversions)
            {
                if (conversion.HostType == host)
                {
                    return conversion;
                }
            }

            var added = CreateConversion(shape, host, _conversions.Count + 1);
            _conversions.Add(added);
            return added;
        }

        private AliasConversion CreateConversion(in KernelParameterShape shape, string host, int key)
        {
            var transfer = dialect.IsCuda ? shape.Cuda : shape.Cpu;
            var allocations = dialect.IsCuda
                ? (shape.MarshalsCudaArrays ? GSharpInternalNames.Launcher.DeviceArrays : "null")
                : GSharpInternalNames.Launcher.AliasHandles;
            return shape.IsArray
                ? new AliasConversion(key, host,
                    ConvertedArrayEmitter.GetElementShadowPath(shape, transfer),
                    transfer.ConvertedElementMarshalMethodName ?? Missing(shape),
                    transfer.ConvertedElementUnmarshalMethodName ?? Missing(shape),
                    allocations)
                : new AliasConversion(key, host,
                    shape.AotStructPath ?? Missing(shape),
                    shape.MarshalMethodName ?? Missing(shape),
                    shape.UnmarshalMethodName ?? Missing(shape),
                    allocations);
        }

        private static string Missing(in KernelParameterShape shape) =>
            throw new InvalidOperationException($"Parameter '{shape.Identifier}' has no conversion of its elements.");
    }
}
