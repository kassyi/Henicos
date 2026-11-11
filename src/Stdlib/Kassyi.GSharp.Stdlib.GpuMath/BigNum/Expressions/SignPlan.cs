namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// The decision for the sign of one node, made from the kinds of its operands' signs before anything is launched
internal readonly record struct SignPlan(
    SignKernel Kernel,
    bool HostNegative,
    ChosenOperand Chosen = ChosenOperand.None,
    bool SignOnHost = false
)
{
    // Nothing is launched for the sign: the host computes the node with HostNegative
    public bool IsHost => Kernel == SignKernel.None;

    // The result's sign is HostNegative, also when a kernel computes the magnitude (Select of the same host signs)
    public bool HostSign => IsHost || SignOnHost;

    public static SignPlan Host(bool negative) => new(SignKernel.None, negative);

    public static SignPlan Device(SignKernel kernel) => new(kernel, HostNegative: false);

    // Max or Min of operands with the same host sign: the operand is a value (a comparison on the device), the sign is
    // that host sign
    public static SignPlan SelectWithHostSign(bool negative) => new(SignKernel.Select, negative, SignOnHost: true);

    // Max or Min of operands with different host signs: the operand with the given sign, for every value, without a
    // comparison
    public static SignPlan Choose(ChosenOperand operand, bool negative) => new(SignKernel.None, negative, operand);
}
