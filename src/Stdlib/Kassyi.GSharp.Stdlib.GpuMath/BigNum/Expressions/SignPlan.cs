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

// A signed value inside the evaluation of an expression: its magnitude and the source of its sign. The public
// GpuBigInt keeps a host sign; a device sign is read once, at the root.
internal readonly struct SignedValue([TakesOwnership] GpuBigUInt magnitude, [TakesOwnership] SignSource sign)
    : IDisposable
{
    public GpuBigUInt Magnitude { get; } = magnitude;

    public SignSource Sign { get; } = sign;

    public static SignedValue FromHost(GpuBigInt value) => new(value.Value, SignSource.Host(value.IsNegative));

    // The value with its host sign; a device sign must have been read before
    public GpuBigInt ToHost() =>
        Sign.IsDevice
            ? throw new InvalidOperationException("The sign of the value is on the device and has not been read.")
            : new GpuBigInt(Magnitude, Sign.HostNegative);

    // Nested, so that a magnitude whose release throws does not keep the sign word
    public void Dispose()
    {
        try
        {
            Magnitude.Dispose();
        }
        finally
        {
            Sign.Dispose();
        }
    }
}

// The signed operations, as the sign planner sees them
internal enum SignedOperation
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    Negate,
    Not,
    Max,
    Min,
    Fit
}

// What computes the sign of a result
internal enum SignKernel
{
    // The host knows the sign: SignPlan.HostNegative
    None,

    // No launch: the result takes over the operand's device word as it is (Fit of a value with a device sign), so the
    // sign moves with the word's ownership
    Forward,

    // The sign word of the result is the XOR of the operands' (or of the operand and 1, for -x)
    Xor,

    // One kernel adds or subtracts the magnitudes by their effective signs and writes the sign (borrow, zero is +)
    AddSubtract,

    // One kernel compares the operands, copies the one Max or Min chooses and writes its sign
    Select,

    // The sign of a remainder: the dividend's when the remainder is not zero, + otherwise
    RemainderSign,

    // ~x == -x - 1 by the operand's sign: -(|x| + 1) for +, |x| - 1 for -, and -1 for a negative zero
    Not
}

// The operand a host plan of Max or Min takes as the result, when the host signs alone decide it
internal enum ChosenOperand
{
    None,
    First,
    Second
}

// The decision for the sign of one node, made from the kinds of its operands' signs before anything is launched
internal readonly record struct SignPlan(
    SignKernel Kernel,
    bool HostNegative,
    ChosenOperand Chosen = ChosenOperand.None
)
{
    public bool IsHost => Kernel == SignKernel.None;

    public static SignPlan Host(bool negative) => new(SignKernel.None, negative);

    public static SignPlan Device(SignKernel kernel) => new(kernel, HostNegative: false);

    // Max or Min of operands with different host signs: the operand with the given sign, for every value, without a
    // comparison
    public static SignPlan Choose(ChosenOperand operand, bool negative) => new(SignKernel.None, negative, operand);
}

// The sign rules of the signed nodes (C# and BigInteger semantics), with the host deciding only what it knows without
// reading the device. A sign that depends on the values, or on an operand's device sign, is planned as a kernel that
// writes a device word.
internal static class SignPlanner
{
    public static SignPlan For(SignedOperation operation, SignSource first, SignSource second = default) =>
        operation switch
        {
            SignedOperation.Add => PlanAddSubtract(first, second, subtract: false),
            SignedOperation.Subtract => PlanAddSubtract(first, second, subtract: true),
            SignedOperation.Multiply or SignedOperation.Divide => PlanProductSign(first, second),
            SignedOperation.Remainder => PlanRemainder(first),
            SignedOperation.Negate => PlanNegate(first),
            SignedOperation.Not => PlanNot(first),
            SignedOperation.Max => PlanSelect(first, second, max: true),
            SignedOperation.Min => PlanSelect(first, second, max: false),
            SignedOperation.Fit => PlanFit(first),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown signed operation.")
        };

    // Magnitudes of the same effective sign add with that sign; different signs subtract, and the borrow (a value)
    // decides the sign
    private static SignPlan PlanAddSubtract(SignSource first, SignSource second, bool subtract)
    {
        if (first.IsDevice || second.IsDevice)
        {
            return SignPlan.Device(SignKernel.AddSubtract);
        }

        var effectiveSecond = second.HostNegative ^ subtract;
        return first.HostNegative == effectiveSecond
            ? SignPlan.Host(first.HostNegative)
            : SignPlan.Device(SignKernel.AddSubtract);
    }

    // A product or quotient: the XOR of the operand signs
    private static SignPlan PlanProductSign(SignSource first, SignSource second) =>
        first.IsDevice || second.IsDevice
            ? SignPlan.Device(SignKernel.Xor)
            : SignPlan.Host(first.HostNegative ^ second.HostNegative);

    // The sign of the dividend, but + for a zero remainder (a value); a non-negative dividend gives +
    private static SignPlan PlanRemainder(SignSource dividend) =>
        !dividend.IsDevice && !dividend.HostNegative
            ? SignPlan.Host(false)
            : SignPlan.Device(SignKernel.RemainderSign);

    private static SignPlan PlanNegate(SignSource operand) =>
        operand.IsDevice ? SignPlan.Device(SignKernel.Xor) : SignPlan.Host(!operand.HostNegative);

    // ~x == -x - 1: negative for a non-negative x; for a negative x, + unless x is -0 (a value)
    private static SignPlan PlanNot(SignSource operand) =>
        !operand.IsDevice && !operand.HostNegative
            ? SignPlan.Host(true)
            : SignPlan.Device(SignKernel.Not);

    // Different host signs decide Max and Min for every value: Max is the non-negative operand, Min the negative one.
    // Otherwise which operand is chosen is a value.
    private static SignPlan PlanSelect(SignSource first, SignSource second, bool max)
    {
        if (first.IsDevice || second.IsDevice || first.HostNegative == second.HostNegative)
        {
            return SignPlan.Device(SignKernel.Select);
        }

        // The chosen operand is the negative one for Min and the other one for Max
        var chosenNegative = !max;
        var chosen = first.HostNegative == chosenNegative ? ChosenOperand.First : ChosenOperand.Second;
        return SignPlan.Choose(chosen, chosenNegative);
    }

    private static SignPlan PlanFit(SignSource operand) =>
        operand.IsDevice ? SignPlan.Device(SignKernel.Forward) : SignPlan.Host(operand.HostNegative);
}
