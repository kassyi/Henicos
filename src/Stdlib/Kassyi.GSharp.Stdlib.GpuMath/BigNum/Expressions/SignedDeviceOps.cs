namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// The device side of the sign plans (SignPlanner): each operation launches one kernel that reads the operands' signs
// (host flags or device words) and writes the result's sign into a word it rents, so nothing is read on the host. In
// CPU fallback mode the same rule runs with BigInteger and gives a host sign.
internal static class SignedDeviceOps
{
    // a + b or a - b into result (at least one word wider than the wider magnitude)
    public static SignSource AddSubtract(SignedValue a, SignedValue b, bool subtract, GpuBigUInt result)
    {
        var workspace = result.Workspace;
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            var value = subtract ? Read(a) - Read(b) : Read(a) + Read(b);
            Write(BigInteger.Abs(value), result);
            return SignSource.Host(value.Sign < 0);
        }

        var word = workspace.RentTemp(1);
        var (aWord, aHost) = Arguments(a.Sign, word);
        var (bWord, bHost) = Arguments(b.Sign, word);
        GpuBigIntSignedKernels.LaunchSignedAddSubKernelPtr(
            a.Magnitude.ToProxy(), a.Magnitude.NumWords, aWord, aHost,
            b.Magnitude.ToProxy(), b.Magnitude.NumWords, bWord, bHost, subtract ? 1 : 0,
            result.ToProxy(), result.NumWords, word.ToProxy(),
            gridDimX: 1, blockDimX: BigIntWideOps.SingleBlockSize(result.NumWords), hStream: workspace.Stream);
        return SignSource.Device(word);
    }

    // Max(a, b) or Min(a, b) into result (at least as wide as the wider magnitude); equal values choose a
    public static SignSource Select(SignedValue a, SignedValue b, bool max, GpuBigUInt result)
    {
        var workspace = result.Workspace;
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            var (left, right) = (Read(a), Read(b));
            var value = max ? (left >= right ? left : right) : (left <= right ? left : right);
            Write(BigInteger.Abs(value), result);
            return SignSource.Host(value.Sign < 0);
        }

        var word = workspace.RentTemp(1);
        var (aWord, aHost) = Arguments(a.Sign, word);
        var (bWord, bHost) = Arguments(b.Sign, word);
        GpuBigIntSignedKernels.LaunchSignedSelectKernelPtr(
            a.Magnitude.ToProxy(), a.Magnitude.NumWords, aWord, aHost,
            b.Magnitude.ToProxy(), b.Magnitude.NumWords, bWord, bHost, max ? 1 : 0,
            result.ToProxy(), result.NumWords, word.ToProxy(),
            gridDimX: 1, blockDimX: BigIntWideOps.SingleBlockSize(result.NumWords), hStream: workspace.Stream);
        return SignSource.Device(word);
    }

    // ~x (-x - 1) into result (at least one word wider than the magnitude)
    public static SignSource Not(SignedValue x, GpuBigUInt result)
    {
        var workspace = result.Workspace;
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            var value = -Read(x) - 1;
            Write(BigInteger.Abs(value), result);
            return SignSource.Host(value.Sign < 0);
        }

        var word = workspace.RentTemp(1);
        var (xWord, xHost) = Arguments(x.Sign, word);
        GpuBigIntSignedKernels.LaunchSignedNotKernelPtr(
            x.Magnitude.ToProxy(), x.Magnitude.NumWords, xWord, xHost, result.ToProxy(), result.NumWords,
            word.ToProxy(),
            gridDimX: 1, blockDimX: BigIntWideOps.SingleBlockSize(result.NumWords), hStream: workspace.Stream);
        return SignSource.Device(word);
    }

    // The sign of a remainder already in place: the dividend's, or + for a zero remainder
    public static SignSource RemainderSign(GpuBigUInt remainder, SignSource dividend)
    {
        var workspace = remainder.Workspace;
        if (GpuKernelLoader.IsCpuFallbackMode)
        {
            return SignSource.Host(dividend.HostNegative && !remainder.ToBigInteger().IsZero);
        }

        var word = workspace.RentTemp(1);
        var (dWord, dHost) = Arguments(dividend, word);
        GpuBigIntSignedKernels.LaunchRemainderSignKernelPtr(
            remainder.ToProxy(), remainder.NumWords, dWord, dHost, word.ToProxy(),
            gridDimX: 1, blockDimX: BigIntWideOps.SingleBlockSize(remainder.NumWords), hStream: workspace.Stream);
        return SignSource.Device(word);
    }

    // a ^ b: the sign of a product or quotient; with a host true, -x; with a host false, a copy
    public static SignSource Xor(GpuWorkspace workspace, SignSource a, SignSource b)
    {
        if (!a.IsDevice && !b.IsDevice)
        {
            return SignSource.Host(a.HostNegative ^ b.HostNegative);
        }

        var word = workspace.RentTemp(1);
        var (aWord, aHost) = Arguments(a, word);
        var (bWord, bHost) = Arguments(b, word);
        GpuBigIntSignedKernels.LaunchSignXorKernelPtr(aWord, aHost, bWord, bHost, word.ToProxy(),
            gridDimX: 1, blockDimX: 32, hStream: workspace.Stream);
        return SignSource.Device(word);
    }

    // Reads a device sign once (one 4-byte copy, which waits for the stream). A host sign is returned as is.
    public static bool ReadNegative(GpuWorkspace workspace, SignSource sign)
    {
        if (!sign.IsDevice)
        {
            return sign.HostNegative;
        }

        var host = ArrayPool<uint>.Shared.Rent(1);
        try
        {
            workspace.Provider.CopyDeviceToHost(host, sign.Word.DevicePointer, 1, workspace.Stream);
            return host[0] != 0u;
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(host);
        }
    }

    // The kernel arguments of a sign: the device word and -1, or the host sign with a placeholder word (the result's
    // word, which the kernel does not read for a host sign; a launch takes no null array)
    private static (GSharpArrayProxy Word, int HostSign) Arguments(SignSource sign, GpuBigUInt placeholder) =>
        sign.IsDevice ? (sign.Word.ToProxy(), -1) : (placeholder.ToProxy(), sign.HostNegative ? 1 : 0);

    // CPU fallback: the value of an operand, whose sign is a host sign there
    private static BigInteger Read(SignedValue value)
    {
        var magnitude = value.Magnitude.ToBigInteger();
        return value.Sign.HostNegative ? -magnitude : magnitude;
    }

    // CPU fallback: value into the words of result (the buffers are host memory there)
    private static void Write(BigInteger value, GpuBigUInt result)
    {
        var words = new uint[result.NumWords];
        value.WriteLimbs(words);
        result.Workspace.Provider.CopyHostToDevice(result.DevicePointer, words, words.Length, result.Workspace.Stream);
    }
}
