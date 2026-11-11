namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Defines advanced arbitrary-precision mathematical operations including NTT multiplication, modular inverses, GCD, and iterative reciprocal/rsqrt solvers.
/// </summary>
public interface IGpuAdvancedMathProvider
{
    /// <summary>
    /// Multiplies two arbitrary-precision unsigned integers using GPU NTT or Comba/Karatsuba algorithms.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The multiplicand.</param>
    /// <param name="rightOperand">The multiplier.</param>
    /// <param name="resultBuffer">The destination buffer where the product will be stored.</param>
    void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Multiplies an arbitrary-precision integer by a 32-bit unsigned scalar on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The big integer operand.</param>
    /// <param name="rightOperand">The 32-bit scalar multiplier.</param>
    /// <param name="resultBuffer">The destination buffer, at least as wide as <paramref name="leftOperand"/>; the
    /// result width rules and domain errors are those of
    /// <see cref="GpuBigIntMath.Mul(GpuBigUIntView, uint, GpuBigUIntView)"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="resultBuffer"/> is narrower than
    /// <paramref name="leftOperand"/>.</exception>
    void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Executes a high-precision inverse square root algorithm using iterative Newton-Raphson approximation kernels on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetOperand">The target input operand.</param>
    /// <param name="resultBuffer">The destination buffer for the computed reciprocal square root.</param>
    /// <param name="marginWords">The safety limb margin allocated for intermediate expansion.</param>
    /// <param name="actualBits">The target precision in bits.</param>
    /// <param name="iterations">The number of Newton-Raphson refinement iterations.</param>
    /// <remarks>
    /// The result is exactly <c>floor(2^B / sqrt(a))</c> with <c>B = (targetOperand.NumWords + marginWords) * 32</c>,
    /// the largest <c>x</c> with <c>a * x^2 &lt;= 2^(2B)</c>: the GPU provider corrects the Newton-Raphson result on the
    /// device after the iterations. <paramref name="resultBuffer"/> holds the initial approximation on entry. When the
    /// correction does not converge, the next read of the stream throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords,
        int actualBits, int iterations);

    /// <summary>
    /// Corrects an integer square root estimate <c>r</c> of <c>a</c> to <c>floor(sqrt(a))</c>.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="operand">The operand <c>a</c>.</param>
    /// <param name="rootSquared">The square <c>r^2</c> of the estimate, with as many words as <paramref name="operand"/>.</param>
    /// <param name="root">The estimate <c>r</c>; receives <c>floor(sqrt(a))</c>. <c>2r + 1</c> must fit in
    /// <c>operand.NumWords + 1</c> words.</param>
    /// <param name="slack">Scratch buffer for the remainder slack, of at least <c>operand.NumWords + 1</c> words.</param>
    /// <param name="trial">Scratch buffer for the trial step, of at least <c>operand.NumWords + 1</c> words.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rootSquared"/>, <paramref name="slack"/> or
    /// <paramref name="trial"/> is shorter than stated above.</exception>
    /// <remarks>
    /// The GPU provider computes the slack <c>a - r^2</c> on the device and moves <c>r</c> by at most 4 steps, without a
    /// host synchronization; an estimate that is <c>floor(sqrt(a))</c> or one less needs at most one. When 4 steps do
    /// not suffice, the next read of the stream throws <see cref="InvalidOperationException"/>. The CPU provider
    /// computes <c>floor(sqrt(a))</c> directly.
    /// </remarks>
    void SqrtFloorCorrection(GpuWorkspace workspace, GpuBigUInt operand, GpuBigUInt rootSquared, GpuBigUInt root,
        GpuBigUInt slack, GpuBigUInt trial);

    /// <summary>
    /// Executes a high-precision reciprocal division algorithm using Newton-Raphson approximation kernels on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="targetOperand">The target divisor operand.</param>
    /// <param name="resultBuffer">The destination buffer for the reciprocal result.</param>
    /// <param name="marginWords">The margin limbs allocated for internal scratch precision.</param>
    /// <param name="iterations">The number of Newton-Raphson iterations.</param>
    /// <param name="targetBits">The final desired precision in bits.</param>
    /// <remarks>
    /// The result is exactly <c>floor(2^targetBits / b)</c>: the GPU provider corrects the Newton-Raphson result on the
    /// device after the iterations. <paramref name="resultBuffer"/> holds the initial approximation on entry. When the
    /// correction does not converge, the next read of the stream throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords,
        int iterations, int targetBits);

    /// <summary>
    /// Computes the greatest common divisor (GCD) of two arbitrary-precision integers using Lehmer / Binary GCD kernels on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The first integer operand.</param>
    /// <param name="rightOperand">The second integer operand.</param>
    /// <param name="resultBuffer">The destination buffer where the GCD will be stored.</param>
    void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the modular multiplicative inverse (<c>value^-1 mod modulus</c>) on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="value">The value to invert.</param>
    /// <param name="modulus">The modulus.</param>
    /// <param name="resultBuffer">The destination buffer for the modular inverse.</param>
    void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
}
