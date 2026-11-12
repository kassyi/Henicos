namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Where the sign of a signed value comes from: a flag the host knows (the sign of a leaf, or a sign the host can
// derive without reading the device), or one word on the device that a kernel wrote (a sign that depends on the
// values: the borrow of a difference, the operand Max chooses, a zero remainder). A device word lets a sign-dependent
// node run without a host read, so a CUDA graph can capture it and replay it for new values.
internal readonly struct SignSource : IDisposable
{
    private SignSource(bool isDevice, bool hostNegative, [TakesOwnership] GpuBigUInt word)
    {
        IsDevice = isDevice;
        HostNegative = hostNegative;
        Word = word;
    }

    // Whether the sign is the device word (otherwise HostNegative)
    public bool IsDevice { get; }

    // The sign when it is known on the host
    public bool HostNegative { get; }

    // One word on the device, 1 for negative, when IsDevice; owned by this source
    public GpuBigUInt Word { get; }

    public static SignSource Host(bool negative) => new(isDevice: false, negative, default);

    public static SignSource Device([TakesOwnership] GpuBigUInt word) => new(isDevice: true, hostNegative: false, word);

    // Returns the device word, if any (a default word owns nothing)
    public void Dispose() => Word.Dispose();
}
