namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents <c>cublasOperation_t</c>, the operation applied to a matrix operand.</summary>
public enum CuBlasOperation
{
    /// <summary>The matrix is used as-is (<c>CUBLAS_OP_N</c>).</summary>
    None = 0,

    /// <summary>The matrix is transposed (<c>CUBLAS_OP_T</c>).</summary>
    Transpose = 1,

    /// <summary>The matrix is conjugate-transposed (<c>CUBLAS_OP_C</c>).</summary>
    ConjugateTranspose = 2
}
