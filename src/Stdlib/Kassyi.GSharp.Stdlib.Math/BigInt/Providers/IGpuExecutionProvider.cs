namespace Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

/// <summary>
/// Defines the contract for executing GPU-accelerated BigInteger arithmetic algorithms and managing device memory.
/// </summary>
public interface IGpuExecutionProvider
{
    /// <summary>Allocates unmanaged global device memory for the specified word count.</summary>
    /// <param name="numWords">The number of 32-bit words to allocate.</param>
    /// <returns>A pointer to the allocated device memory.</returns>
    IntPtr AllocateMemory(int numWords);

    /// <summary>Frees the allocated unmanaged global device memory.</summary>
    /// <param name="devicePointer">The pointer to the device memory to free.</param>
    void FreeMemory(IntPtr devicePointer);

    /// <summary>Copies limb data from host managed memory to GPU device memory.</summary>
    /// <param name="devicePointer">The destination pointer on the device.</param>
    /// <param name="hostData">The source data array on the host.</param>
    /// <param name="numWords">The number of 32-bit words to copy.</param>
    void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords);

    /// <summary>Copies limb data from GPU device memory to host managed memory.</summary>
    /// <param name="hostData">The destination array on the host.</param>
    /// <param name="devicePointer">The source pointer on the device.</param>
    /// <param name="numWords">The number of 32-bit words to copy.</param>
    void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords);

    /// <summary>Copies limb data between two GPU device memory locations.</summary>
    /// <param name="destPointer">The destination pointer on the device.</param>
    /// <param name="srcPointer">The source pointer on the device.</param>
    /// <param name="numWords">The number of 32-bit words to copy.</param>
    void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords);

    /// <summary>Reads a single 32-bit word directly from device memory.</summary>
    /// <param name="devicePointer">The base device pointer.</param>
    /// <param name="wordOffset">The offset in words.</param>
    /// <returns>The 32-bit unsigned integer value.</returns>
    uint ReadWord(IntPtr devicePointer, int wordOffset = 0);

    /// <summary>Writes a single 32-bit word directly to device memory.</summary>
    /// <param name="devicePointer">The base device pointer.</param>
    /// <param name="wordOffset">The offset in words.</param>
    /// <param name="value">The 32-bit unsigned integer value to write.</param>
    void WriteWord(IntPtr devicePointer, int wordOffset, uint value);

    /// <summary>Computes addition on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Add(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes subtraction on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Sub(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes multiplication on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Mul(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes scalar addition on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The scalar second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Add(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes scalar subtraction on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The scalar second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Sub(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes scalar multiplication on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The scalar second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Mul(GpuContext context, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes bitwise AND on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void BitwiseAnd(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes bitwise OR on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void BitwiseOr(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes bitwise XOR on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void BitwiseXor(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes bitwise NOT on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void BitwiseNot(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes left bit shift on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand to shift.</param>
    /// <param name="shiftBits">The number of bits to shift.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void ShiftLeft(GpuContext context, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes right bit shift on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand to shift.</param>
    /// <param name="shiftBits">The number of bits to shift.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void ShiftRight(GpuContext context, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);

    /// <summary>Computes in-place self squaring on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="devicePointer">The pointer to the device memory containing the operand.</param>
    /// <param name="logicalWords">The logical number of words of the operand.</param>
    /// <returns><c>true</c> if successful; otherwise, <c>false</c>.</returns>
    bool Square(GpuContext context, IntPtr devicePointer, int logicalWords);

    /// <summary>Computes Greatest Common Divisor on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <param name="resultBuffer">The buffer to store the result.</param>
    void Gcd(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);

    /// <summary>Checks whether operand equals zero on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand to check.</param>
    /// <returns><c>true</c> if the operand is zero; otherwise, <c>false</c>.</returns>
    bool IsZero(GpuContext context, GpuUIntBigInteger leftOperand);

    /// <summary>Checks whether operand equals one on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand to check.</param>
    /// <returns><c>true</c> if the operand is one; otherwise, <c>false</c>.</returns>
    bool IsOne(GpuContext context, GpuUIntBigInteger leftOperand);

    /// <summary>Gets the bit length of the operand on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The operand.</param>
    /// <returns>The bit length.</returns>
    int GetBitLength(GpuContext context, GpuUIntBigInteger leftOperand);

    /// <summary>Compares two operands on GPU.</summary>
    /// <param name="context">The GPU execution context.</param>
    /// <param name="leftOperand">The first operand.</param>
    /// <param name="rightOperand">The second operand.</param>
    /// <returns>A signed integer that indicates the relative values of the operands.</returns>
    int CompareTo(GpuContext context, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand);

    /// <summary>Begins capturing CUDA commands into a graph via the specified stream.</summary>
    void BeginCapture(IntPtr stream);

    /// <summary>Ends capturing CUDA commands and returns the captured graph.</summary>
    /// <returns>A pointer to the captured CUDA graph.</returns>
    IntPtr EndCapture(IntPtr stream);

    /// <summary>Instantiates a CUDA graph into an executable graph.</summary>
    /// <param name="graph">The CUDA graph pointer.</param>
    /// <returns>A pointer to the executable graph.</returns>
    IntPtr InstantiateGraph(IntPtr graph);

    /// <summary>Launches an executable CUDA graph on the specified stream.</summary>
    /// <param name="graphExec">The executable graph pointer.</param>
    void LaunchGraph(IntPtr graphExec, IntPtr stream);

    /// <summary>Destroys a CUDA graph and its executable counterpart.</summary>
    /// <param name="graph">The CUDA graph pointer.</param>
    /// <param name="graphExec">The executable graph pointer.</param>
    void DestroyGraph(IntPtr graph, IntPtr graphExec);
}


