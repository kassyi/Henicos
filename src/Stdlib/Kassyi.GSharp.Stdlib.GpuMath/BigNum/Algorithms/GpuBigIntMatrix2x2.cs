namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Represents a 2x2 transition matrix of arbitrary-precision integers (<see cref="GpuBigUInt"/>) allocated in GPU device memory.
/// </summary>
/// <remarks>
/// <para>
/// This matrix structure is used to accelerate the Half-GCD (HGCD), Lehmer's GCD, and Extended Euclidean Algorithm (EEA)
/// pipelines on NVIDIA GPUs by compounding 2x2 quotient transition matrices into cumulative matrix products.
/// </para>
/// <para><b>Thread Safety:</b> Static methods are thread-safe. Instance methods and properties are not thread-safe and must not be accessed concurrently across multiple threads without external synchronization.</para>
/// <para><b>Memory Management:</b> Implements <see cref="IDisposable"/> to ensure deterministic deallocation of GPU global memory allocated for all four matrix elements (<see cref="M00"/>, <see cref="M01"/>, <see cref="M10"/>, and <see cref="M11"/>).</para>
/// <para><b>Execution Target:</b> Host-only (CPU) control API that dispatches arithmetic operations and memory transfers to the GPU.</para>
/// </remarks>
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
public class GpuBigIntMatrix2x2 : IDisposable
{
    /// <summary>
    /// Gets the <see cref="GpuBigUInt"/> element located at row 0, column 0 ($M_{00}$) in GPU device memory.
    /// </summary>
    /// <value>
    /// The arbitrary-precision integer representing the top-left element of the matrix.
    /// </value>
    public GpuBigUInt M00
    {
        get;
    }

    /// <summary>
    /// Gets the <see cref="GpuBigUInt"/> element located at row 0, column 1 ($M_{01}$) in GPU device memory.
    /// </summary>
    /// <value>
    /// The arbitrary-precision integer representing the top-right element of the matrix.
    /// </value>
    public GpuBigUInt M01
    {
        get;
    }

    /// <summary>
    /// Gets the <see cref="GpuBigUInt"/> element located at row 1, column 0 ($M_{10}$) in GPU device memory.
    /// </summary>
    /// <value>
    /// The arbitrary-precision integer representing the bottom-left element of the matrix.
    /// </value>
    public GpuBigUInt M10
    {
        get;
    }

    /// <summary>
    /// Gets the <see cref="GpuBigUInt"/> element located at row 1, column 1 ($M_{11}$) in GPU device memory.
    /// </summary>
    /// <value>
    /// The arbitrary-precision integer representing the bottom-right element of the matrix.
    /// </value>
    public GpuBigUInt M11
    {
        get;
    }

    /// <summary>
    /// Gets a value indicating whether this matrix represents an odd number of reduction steps in the Euclidean algorithm.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the cumulative reduction step count is odd; otherwise, <see langword="false"/>.
    /// </value>
    /// <remarks>
    /// In Half-GCD computations, step parity determines the sign and operand ordering when applying the matrix to reduce operand pairs.
    /// </remarks>
    public bool IsOddStep
    {
        get;
    }

    /// <summary>
    /// Gets the parent <see cref="GpuWorkspace"/> managing the GPU device memory and execution provider for this matrix.
    /// </summary>
    /// <value>
    /// The active GPU context associated with this matrix and all its constituent elements.
    /// </value>
    public GpuWorkspace Workspace
    {
        get;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GpuBigIntMatrix2x2"/> class with uninitialized memory for each matrix element.
    /// </summary>
    /// <param name="workspace">The parent GPU execution context. Cannot be <see langword="null"/>.</param>
    /// <param name="numWords">The number of 32-bit limbs (words) to allocate for each matrix element. Must be greater than zero.</param>
    /// <param name="isOddStep">A value indicating whether this transition matrix represents an odd number of reduction steps.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="numWords"/> is less than or equal to zero.</exception>
    /// <exception cref="OutOfMemoryException">Thrown when GPU global memory allocation fails for any of the matrix elements.</exception>
    public GpuBigIntMatrix2x2(GpuWorkspace workspace, int numWords, bool isOddStep)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numWords);

        Workspace = workspace;
        IsOddStep = isOddStep;

        try
        {
            M00 = new GpuBigUInt(workspace, numWords);
            M01 = new GpuBigUInt(workspace, numWords);
            M10 = new GpuBigUInt(workspace, numWords);
            M11 = new GpuBigUInt(workspace, numWords);

            workspace.Track(this);
        }
        catch
        {
            M00.Dispose();
            M01.Dispose();
            M10.Dispose();
            M11.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates and initializes a 2x2 identity matrix ($I = \begin{pmatrix} 1 &amp; 0 \\ 0 &amp; 1 \end{pmatrix}$) in GPU device memory.
    /// </summary>
    /// <param name="workspace">The parent GPU execution context. Cannot be <see langword="null"/>.</param>
    /// <param name="numWords">The number of 32-bit limbs (words) to allocate for each matrix element. Must be greater than zero.</param>
    /// <returns>
    /// A new <see cref="GpuBigIntMatrix2x2"/> instance representing the identity matrix, where <see cref="M00"/> and <see cref="M11"/> are set to 1, and <see cref="M01"/> and <see cref="M10"/> are set to 0.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="workspace"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="numWords"/> is less than or equal to zero.</exception>
    /// <exception cref="OutOfMemoryException">Thrown when GPU global memory allocation fails.</exception>
    /// <remarks>
    /// <para><b>Lifecycle:</b> The caller is responsible for disposing the returned matrix to release allocated GPU device memory.</para>
    /// </remarks>
    public static GpuBigIntMatrix2x2 Identity(GpuWorkspace workspace, int numWords)
    {
        var mat = new GpuBigIntMatrix2x2(workspace, numWords, false);

        mat.M00.Clear();
        workspace.Provider.CopyHostToDevice(mat.M00.DevicePointer, [1u], 1, workspace.Stream);

        mat.M11.Clear();
        workspace.Provider.CopyHostToDevice(mat.M11.DevicePointer, [1u], 1, workspace.Stream);

        // [WHY] Explicitly initialize off-diagonal elements to 0 on the GPU to ensure deterministic state regardless of driver-level allocation zeroing.
        mat.M01.Clear();
        mat.M10.Clear();

        return mat;
    }

    /// <summary>
    /// Multiplies two 2x2 GPU BigInteger matrices (<paramref name="v"/> and <paramref name="u"/>) and returns their product ($R = V \times U$).
    /// </summary>
    /// <param name="v">The left-hand operand matrix $V$. Cannot be <see langword="null"/>.</param>
    /// <param name="u">The right-hand operand matrix $U$. Cannot be <see langword="null"/>.</param>
    /// <returns>
    /// A new <see cref="GpuBigIntMatrix2x2"/> instance containing the product of <paramref name="v"/> and <paramref name="u"/>.
    /// The word count of each element in the resulting matrix equals the sum of the operand limb lengths (<c>v.M00.NumWords + u.M00.NumWords</c>).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="v"/> or <paramref name="u"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when <paramref name="v"/> or <paramref name="u"/> has been disposed.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="v"/> and <paramref name="u"/> belong to different <see cref="GpuWorkspace"/> instances.</exception>
    /// <remarks>
    /// <para>
    /// Computes the 2x2 matrix product using GPU arithmetic kernels:
    /// <list type="bullet">
    /// <item><description>$R_{00} = V_{00} \cdot U_{00} + V_{01} \cdot U_{10}$</description></item>
    /// <item><description>$R_{01} = V_{00} \cdot U_{01} + V_{01} \cdot U_{11}$</description></item>
    /// <item><description>$R_{10} = V_{10} \cdot U_{00} + V_{11} \cdot U_{10}$</description></item>
    /// <item><description>$R_{11} = V_{10} \cdot U_{01} + V_{11} \cdot U_{11}$</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The step parity of the result is computed as <c>v.IsOddStep ^ u.IsOddStep</c>.
    /// </para>
    /// <para><b>Lifecycle:</b> The caller is responsible for disposing the returned matrix.</para>
    /// </remarks>
    public static GpuBigIntMatrix2x2 Multiply(GpuBigIntMatrix2x2 v, GpuBigIntMatrix2x2 u)
    {
        ArgumentNullException.ThrowIfNull(v);
        ArgumentNullException.ThrowIfNull(u);
        ObjectDisposedException.ThrowIf(v._disposed, v);
        ObjectDisposedException.ThrowIf(u._disposed, u);

        if (v.Workspace != u.Workspace)
        {
            throw new ArgumentException("Matrices must belong to the same GPU workspace.", nameof(u));
        }

        var workspace = v.Workspace;
        var outWords = v.M00.NumWords + u.M00.NumWords;
        var result = new GpuBigIntMatrix2x2(workspace, outWords, v.IsOddStep ^ u.IsOddStep);

        try
        {
            using var temp1 = new GpuBigUInt(workspace, outWords);
            using var temp2 = new GpuBigUInt(workspace, outWords);

            ComputeMatrixElement(v.M00, u.M00, v.M01, u.M10, temp1, temp2, result.M00);
            ComputeMatrixElement(v.M00, u.M01, v.M01, u.M11, temp1, temp2, result.M01);
            ComputeMatrixElement(v.M10, u.M00, v.M11, u.M10, temp1, temp2, result.M10);
            ComputeMatrixElement(v.M10, u.M01, v.M11, u.M11, temp1, temp2, result.M11);

            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static void ComputeMatrixElement(
        GpuBigUInt left1,
        GpuBigUInt right1,
        GpuBigUInt left2,
        GpuBigUInt right2,
        GpuBigUInt temp1,
        GpuBigUInt temp2,
        GpuBigUInt destination)
    {
        GpuBigIntMath.Mul(left1, right1, temp1);
        GpuBigIntMath.Mul(left2, right2, temp2);
        GpuBigIntMath.Add(temp1, temp2, destination);
    }

    /// <summary>
    /// Applies this 2x2 reduction matrix to a pair of GPU integers (<paramref name="a"/> and <paramref name="b"/>), storing the reduced outputs in <paramref name="outA"/> and <paramref name="outB"/>.
    /// </summary>
    /// <param name="a">The first operand <see cref="GpuBigUInt"/> in the vector $\begin{pmatrix} a \\ b \end{pmatrix}$. Cannot be <see langword="null"/>.</param>
    /// <param name="b">The second operand <see cref="GpuBigUInt"/> in the vector $\begin{pmatrix} a \\ b \end{pmatrix}$. Cannot be <see langword="null"/>.</param>
    /// <param name="outA">The destination <see cref="GpuBigUInt"/> to receive the transformed first element $A'$. Cannot be <see langword="null"/>.</param>
    /// <param name="outB">The destination <see cref="GpuBigUInt"/> to receive the transformed second element $B'$. Cannot be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="a"/>, <paramref name="b"/>, <paramref name="outA"/>, or <paramref name="outB"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">Thrown when this matrix has been disposed.</exception>
    /// <exception cref="ArgumentException">Thrown when any argument does not belong to the same <see cref="GpuWorkspace"/> as this matrix.</exception>
    /// <remarks>
    /// <para>
    /// The linear transformation applied depends on <see cref="IsOddStep"/>:
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// When <see cref="IsOddStep"/> is <see langword="false"/>:
    /// <para>$A' = M_{00} \cdot a - M_{01} \cdot b$</para>
    /// <para>$B' = M_{11} \cdot b - M_{10} \cdot a$</para>
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// When <see cref="IsOddStep"/> is <see langword="true"/>:
    /// <para>$A' = M_{01} \cdot b - M_{00} \cdot a$</para>
    /// <para>$B' = M_{10} \cdot a - M_{11} \cdot b$</para>
    /// </description>
    /// </item>
    /// </list>
    /// </para>
    /// <para>
    /// Destination buffers <paramref name="outA"/> and <paramref name="outB"/> are zero-cleared prior to copying the lower limbs of the calculated results.
    /// </para>
    /// </remarks>
    public void Apply(GpuBigUInt a, GpuBigUInt b, GpuBigUInt outA, GpuBigUInt outB)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (a.Workspace != Workspace || b.Workspace != Workspace || outA.Workspace != Workspace ||
            outB.Workspace != Workspace)
        {
            throw new ArgumentException("All operands must belong to the same GPU workspace as the matrix.");
        }

        var workspace = Workspace;
        var wordsA = M00.NumWords + a.NumWords;
        var wordsB = M01.NumWords + b.NumWords;
        var tempWords = Math.Max(wordsA, wordsB);

        using var rawA1 = new GpuBigUInt(workspace, wordsA);
        using var rawA2 = new GpuBigUInt(workspace, wordsB);
        using var rawB1 = new GpuBigUInt(workspace, wordsA);
        using var rawB2 = new GpuBigUInt(workspace, wordsB);

        GpuBigIntMath.Mul(M00, a, rawA1);
        GpuBigIntMath.Mul(M01, b, rawA2);
        GpuBigIntMath.Mul(M10, a, rawB1);
        GpuBigIntMath.Mul(M11, b, rawB2);

        using GpuBigUInt? padA1 = wordsA == tempWords ? null : rawA1.PadTo(tempWords);
        using GpuBigUInt? padA2 = wordsB == tempWords ? null : rawA2.PadTo(tempWords);
        using GpuBigUInt? padB1 = wordsA == tempWords ? null : rawB1.PadTo(tempWords);
        using GpuBigUInt? padB2 = wordsB == tempWords ? null : rawB2.PadTo(tempWords);

        var tempA1 = padA1 ?? rawA1;
        var tempA2 = padA2 ?? rawA2;
        var tempB1 = padB1 ?? rawB1;
        var tempB2 = padB2 ?? rawB2;

        using var resA = new GpuBigUInt(workspace, tempWords);
        using var resB = new GpuBigUInt(workspace, tempWords);

        var (minuendA, subtrahendA, minuendB, subtrahendB) = IsOddStep
            ? (tempA2, tempA1, tempB1, tempB2)
            : (tempA1, tempA2, tempB2, tempB1);

        GpuBigIntMath.Sub(minuendA, subtrahendA, resA);
        GpuBigIntMath.Sub(minuendB, subtrahendB, resB);

        // [WHY] Destination buffers may have fewer limbs or need cleanup since reduced numbers decrease in magnitude.
        outA.Clear();
        outA.CopyFrom(resA, numWords: Math.Min(outA.NumWords, tempWords));

        outB.Clear();
        outB.CopyFrom(resB, numWords: Math.Min(outB.NumWords, tempWords));
    }

    private bool _disposed;

    /// <summary>
    /// Releases the unmanaged resources used by the <see cref="GpuBigIntMatrix2x2"/> and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            M00.Dispose();
            M01.Dispose();
            M10.Dispose();
            M11.Dispose();
        }

        _disposed = true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Disposes all four constituent <see cref="GpuBigUInt"/> instances (<see cref="M00"/>, <see cref="M01"/>, <see cref="M10"/>, and <see cref="M11"/>) and releases their GPU device memory allocations.
    /// </remarks>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
