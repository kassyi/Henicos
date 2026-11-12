namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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

    // One kernel compares the operands and copies the one Max or Min chooses, its sign flag included (the result is
    // one of the operands); with operands of the same host sign, the sign stays on the host (SignPlan.HostSign)
    Select,

    // The sign of a remainder: the dividend's when the remainder is not zero, + otherwise
    RemainderSign,

    // ~x == -x - 1 by the operand's sign: -(|x| + 1) for +, |x| - 1 for -, and -1 for a negative zero
    Not
}
