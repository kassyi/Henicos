namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Defines low-level GPU memory allocation, host-device transfers, and direct word access methods.
/// </summary>
/// <remarks>
/// <para>
/// <b>Thread Safety:</b> Implementations must handle stream-based synchronization or ensure thread safety across concurrent stream captures.
/// </para>
/// </remarks>
public interface IGpuMemoryProvider
{
    /// <summary>
    /// Allocates unmanaged device global memory for the specified number of 32-bit words.
    /// </summary>
    /// <param name="numWords">The number of 32-bit words (limbs) to allocate.</param>
    /// <returns>An unmanaged pointer (<c>CUdeviceptr</c>) to the allocated device memory segment.</returns>
    IntPtr AllocateMemory(int numWords);

    /// <summary>
    /// Releases previously allocated device global memory.
    /// </summary>
    /// <param name="devicePointer">The unmanaged pointer to the device memory segment to free.</param>
    /// <param name="stream">
    /// The stream that orders the release, or <see cref="IntPtr.Zero"/> for an immediate release.
    /// <list type="bullet">
    ///   <item><description>
    ///   <see cref="IntPtr.Zero"/>: freed immediately. On CUDA this waits for all work on every stream of the device,
    ///   so it is safe even when the memory was used on streams the caller does not know.
    ///   </description></item>
    ///   <item><description>
    ///   A stream: freed once <paramref name="stream"/> reaches this point, without blocking the calling thread.
    ///   The caller guarantees that all work using the memory is ordered on <paramref name="stream"/>,
    ///   and that the stream is not being captured into a CUDA Graph.
    ///   </description></item>
    /// </list>
    /// </param>
    void FreeMemory(IntPtr devicePointer, IntPtr stream = 0);

    /// <summary>
    /// Asynchronously clears the specified device memory region to zero on the given CUDA stream.
    /// </summary>
    /// <param name="devicePointer">The unmanaged pointer to the starting device memory address.</param>
    /// <param name="numWords">The number of 32-bit words to clear.</param>
    /// <param name="stream">The unmanaged CUDA stream handle.</param>
    void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);

    /// <summary>
    /// Copies limb data synchronously or asynchronously from host memory to device global memory.
    /// </summary>
    /// <param name="devicePointer">The destination device memory pointer.</param>
    /// <param name="hostData">The source managed 32-bit unsigned integer array.</param>
    /// <param name="numWords">The number of words to copy.</param>
    /// <param name="stream">The optional CUDA stream handle for asynchronous copy operations.</param>
    void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);

    /// <summary>
    /// Copies limb data from device global memory to host memory and waits until the data has arrived.
    /// </summary>
    /// <param name="hostData">The destination managed 32-bit unsigned integer array.</param>
    /// <param name="devicePointer">The source device memory pointer.</param>
    /// <param name="numWords">The number of words to copy.</param>
    /// <param name="stream">
    /// The stream the copy is ordered on; the copy observes all work enqueued on it before the call.
    /// </param>
    /// <remarks>
    /// Synchronous: <paramref name="hostData"/> holds the data when the method returns, whether the array is pageable or
    /// page-locked, so callers need no additional stream synchronization. Use <see cref="CopyDeviceToHostAsync"/> to
    /// avoid blocking the calling thread while queued work completes.
    /// </remarks>
    void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);

    /// <summary>
    /// Asynchronously copies limb data from device global memory to host memory without blocking the calling thread.
    /// </summary>
    /// <param name="hostData">The destination host memory, at least <paramref name="numWords"/> words long.</param>
    /// <param name="devicePointer">The source device memory pointer.</param>
    /// <param name="numWords">The number of words to copy.</param>
    /// <param name="stream">
    /// The stream the copy is ordered on; the copy observes all work enqueued on it before the call.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that detaches the awaiter. Device work is not canceled, and <paramref name="hostData"/> is not written
    /// after cancellation.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes once <paramref name="hostData"/> holds the data.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="numWords"/> exceeds the length of <paramref name="hostData"/>.
    /// </exception>
    ValueTask CopyDeviceToHostAsync(Memory<uint> hostData, IntPtr devicePointer, int numWords, IntPtr stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies data directly between two device memory allocations.
    /// </summary>
    /// <param name="destPointer">The destination device memory pointer.</param>
    /// <param name="srcPointer">The source device memory pointer.</param>
    /// <param name="numWords">The number of 32-bit words to copy.</param>
    /// <param name="stream">The optional CUDA stream handle for asynchronous copy operations.</param>
    void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);

    /// <summary>
    /// Reads a single 32-bit limb value directly from device memory at the given word offset.
    /// </summary>
    /// <param name="devicePointer">The base device memory pointer.</param>
    /// <param name="wordOffset">The zero-based 32-bit word offset.</param>
    /// <param name="stream">
    /// The stream the read is ordered on, or <see cref="IntPtr.Zero"/> for the legacy null stream.
    /// A stream observes exactly the work enqueued on it and does not wait for unrelated streams.
    /// The null stream additionally waits for all work on every blocking stream of the context.
    /// </param>
    /// <returns>The 32-bit unsigned integer read from the device.</returns>
    /// <remarks>Synchronous: the value is available when the method returns.</remarks>
    uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0);

    /// <summary>
    /// Writes a single 32-bit limb value directly to device memory at the given word offset.
    /// </summary>
    /// <param name="devicePointer">The base device memory pointer.</param>
    /// <param name="wordOffset">The zero-based 32-bit word offset.</param>
    /// <param name="value">The 32-bit unsigned integer value to write.</param>
    /// <param name="stream">
    /// The stream the write is ordered on, or <see cref="IntPtr.Zero"/> for the legacy null stream.
    /// </param>
    /// <remarks>
    /// With a stream the write is only enqueued and the host is not blocked; it is visible to later work enqueued on the
    /// same stream. Without a stream the write completes before the method returns.
    /// </remarks>
    void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0);

    /// <summary>
    /// Blocks the host thread until all previously submitted tasks in the specified CUDA stream have completed.
    /// </summary>
    /// <param name="stream">The unmanaged stream handle to synchronize.</param>
    void SynchronizeStream(IntPtr stream);

    /// <summary>
    /// Asynchronously waits until all previously submitted work in the specified CUDA stream has completed, without
    /// blocking the calling thread.
    /// </summary>
    /// <param name="stream">
    /// The unmanaged stream handle to wait for. <see cref="IntPtr.Zero"/> completes immediately, like
    /// <see cref="SynchronizeStream"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that detaches the awaiter; the stream keeps executing and remains usable.
    /// </param>
    /// <returns>A <see cref="ValueTask"/> that completes when the stream has drained.</returns>
    ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines elementary arbitrary-precision arithmetic, bitwise operations, and relational predicates executed on GPU device kernels.
/// </summary>
public interface IGpuBasicMathProvider
{
    /// <summary>
    /// Adds two arbitrary-precision unsigned integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing kernel parameters.</param>
    /// <param name="leftOperand">The left unsigned integer operand.</param>
    /// <param name="rightOperand">The right unsigned integer operand.</param>
    /// <param name="resultBuffer">The destination buffer where the sum will be written.</param>
    void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Subtracts the right operand from the left operand on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace managing kernel parameters.</param>
    /// <param name="leftOperand">The minuend operand.</param>
    /// <param name="rightOperand">The subtrahend operand.</param>
    /// <param name="resultBuffer">The destination buffer where the difference will be written.</param>
    void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Adds a 32-bit unsigned integer scalar to an arbitrary-precision integer on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The big integer operand.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Subtracts a 32-bit unsigned integer scalar from an arbitrary-precision integer on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The big integer operand.</param>
    /// <param name="rightOperand">The 32-bit unsigned scalar.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the bitwise AND of two arbitrary-precision integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the bitwise OR of two arbitrary-precision integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the bitwise XOR of two arbitrary-precision integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the bitwise NOT (one's complement) of an arbitrary-precision integer on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The source operand.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);

    /// <summary>
    /// Shifts an arbitrary-precision integer left by the specified bit count on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer to shift.</param>
    /// <param name="shiftBits">The number of bit positions to shift left.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);

    /// <summary>
    /// Shifts an arbitrary-precision integer right by the specified bit count on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer to shift.</param>
    /// <param name="shiftBits">The number of bit positions to shift right.</param>
    /// <param name="resultBuffer">The destination buffer.</param>
    void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);

    /// <summary>
    /// Computes the square of a large integer in-place or into the specified pointer.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="devicePointer">The pointer to the device memory containing the limbs.</param>
    /// <param name="logicalWords">The number of active limbs.</param>
    /// <returns><see langword="true"/> if squaring succeeded; otherwise, <see langword="false"/>.</returns>
    bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);

    /// <summary>
    /// Evaluates whether the given arbitrary-precision integer is equal to zero on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer operand to evaluate.</param>
    /// <returns><see langword="true"/> if the value is zero; otherwise, <see langword="false"/>.</returns>
    bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);

    /// <summary>
    /// Evaluates whether the given arbitrary-precision integer is equal to one on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer operand to evaluate.</param>
    /// <returns><see langword="true"/> if the value is one; otherwise, <see langword="false"/>.</returns>
    bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);

    /// <summary>
    /// Computes the active bit length of the given arbitrary-precision integer on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The integer operand.</param>
    /// <returns>The number of significant bits required to represent the integer.</returns>
    int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);

    /// <summary>
    /// Reads the bit length and the leading 64 bits of an arbitrary-precision integer with one wait for the device.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="value">The integer operand.</param>
    /// <param name="bitLength">When this method returns, the number of significant bits of <paramref name="value"/>
    /// (0 for zero).</param>
    /// <param name="leadingBits">When this method returns, the top 64 bits of <paramref name="value"/>, its top set
    /// bit at bit 63 (missing low bits are 0; 0 for zero).</param>
    void ReadLeadingBits(GpuWorkspace workspace, GpuBigUInt value, out int bitLength, out ulong leadingBits);

    /// <summary>
    /// Compares two arbitrary-precision unsigned integers on the GPU.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="leftOperand">The left operand.</param>
    /// <param name="rightOperand">The right operand.</param>
    /// <returns>A signed integer indicating the relative order (<c>-1</c> if left &lt; right, <c>0</c> if equal, <c>1</c> if left &gt; right).</returns>
    int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);

    /// <summary>
    /// Divides an arbitrary-precision unsigned integer by a 32-bit unsigned scalar on the GPU and returns the remainder.
    /// </summary>
    /// <param name="workspace">The GPU workspace.</param>
    /// <param name="dividend">The dividend big integer operand.</param>
    /// <param name="divisor">The 32-bit unsigned scalar divisor.</param>
    /// <param name="quotientBuffer">The destination buffer where the quotient will be written.</param>
    /// <returns>The 32-bit unsigned scalar remainder (<c>dividend % divisor</c>).</returns>
    uint DivideByUInt32(GpuWorkspace workspace, GpuBigUInt dividend, uint divisor, GpuBigUInt quotientBuffer);
}

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
    /// <param name="resultBuffer">The destination buffer.</param>
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
    /// <param name="slack">Scratch of at least <c>operand.NumWords + 1</c> words.</param>
    /// <param name="trial">Scratch of at least <c>operand.NumWords + 1</c> words.</param>
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
    /// <param name="numBits">The base bit precision.</param>
    /// <param name="marginBits">Additional precision guard bits.</param>
    /// <param name="iterations">The number of Newton-Raphson iterations.</param>
    /// <param name="targetBits">The final desired precision in bits.</param>
    /// <remarks>
    /// The result is exactly <c>floor(2^targetBits / b)</c>: the GPU provider corrects the Newton-Raphson result on the
    /// device after the iterations. <paramref name="resultBuffer"/> holds the initial approximation on entry. When the
    /// correction does not converge, the next read of the stream throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords,
        int numBits, int marginBits, int iterations, int targetBits);

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

/// <summary>
/// Defines CUDA Graph capture, instantiation, execution, and cleanup contracts for accelerated sequence execution.
/// </summary>
public interface IGpuGraphProvider
{
    /// <summary>
    /// Begins capturing CUDA kernel launches into a CUDA Graph on the specified stream.
    /// </summary>
    /// <param name="stream">The CUDA stream to capture.</param>
    void BeginCapture(IntPtr stream);

    /// <summary>
    /// Ends capture on the specified stream and returns the resulting unmanaged CUDA Graph handle.
    /// </summary>
    /// <param name="stream">The CUDA stream being captured.</param>
    /// <returns>An unmanaged handle to the captured <c>cudaGraph_t</c>.</returns>
    IntPtr EndCapture(IntPtr stream);

    /// <summary>
    /// Instantiates an executable CUDA Graph instance from a captured graph handle.
    /// </summary>
    /// <param name="graph">The captured <c>cudaGraph_t</c> handle.</param>
    /// <returns>An unmanaged handle to the instantiated <c>cudaGraphExec_t</c>.</returns>
    IntPtr InstantiateGraph(IntPtr graph);

    /// <summary>
    /// Launches an instantiated executable CUDA Graph on the specified stream.
    /// </summary>
    /// <param name="graphExec">The instantiated <c>cudaGraphExec_t</c> handle.</param>
    /// <param name="stream">The stream on which the graph should be queued.</param>
    void LaunchGraph(IntPtr graphExec, IntPtr stream);

    /// <summary>
    /// Releases and destroys the unmanaged CUDA Graph and executable graph instances.
    /// </summary>
    /// <param name="graph">The <c>cudaGraph_t</c> handle to destroy.</param>
    /// <param name="graphExec">The <c>cudaGraphExec_t</c> handle to destroy.</param>
    void DestroyGraph(IntPtr graph, IntPtr graphExec);
}

/// <summary>
/// Defines the aggregated contract for executing GPU-accelerated BigInteger arithmetic algorithms, managing device memory, and capturing CUDA graphs.
/// </summary>
public interface IGpuExecutionProvider :
    IGpuMemoryProvider,
    IGpuBasicMathProvider,
    IGpuAdvancedMathProvider,
    IGpuGraphProvider;

