namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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

    // Max and Min return one of the operands, its sign flag included. Different host signs decide the operand for
    // every value: Max is the non-negative one, Min the negative one. The same host signs leave the operand to a
    // comparison on the device and the sign on the host; a device sign leaves both to the device.
    private static SignPlan PlanSelect(SignSource first, SignSource second, bool max)
    {
        if (first.IsDevice || second.IsDevice)
        {
            return SignPlan.Device(SignKernel.Select);
        }

        if (first.HostNegative == second.HostNegative)
        {
            return SignPlan.SelectWithHostSign(first.HostNegative);
        }

        // The chosen operand is the negative one for Min and the other one for Max
        var chosenNegative = !max;
        var chosen = first.HostNegative == chosenNegative ? ChosenOperand.First : ChosenOperand.Second;
        return SignPlan.Choose(chosen, chosenNegative);
    }

    private static SignPlan PlanFit(SignSource operand) =>
        operand.IsDevice ? SignPlan.Device(SignKernel.Forward) : SignPlan.Host(operand.HostNegative);
}
