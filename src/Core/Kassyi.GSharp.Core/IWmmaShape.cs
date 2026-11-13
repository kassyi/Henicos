namespace Kassyi.GSharp.Core;

/// <summary>
/// Marker interface representing the tile dimensions (<c>m x n x k</c>) of a Tensor Core warp matrix operation (<see cref="Wmma"/>).
/// </summary>
/// <remarks>
/// <para>
/// In a Warp Matrix Multiply and Accumulate (WMMA) operation, all 32 threads of a warp cooperate to compute
/// the matrix multiply-accumulate equation <c>D = A * B + C</c>. The tile dimensions <c>M</c>, <c>N</c>, and <c>K</c>
/// represent:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>M</c>: the number of rows in matrix <c>A</c>, matrix <c>C</c>, and result matrix <c>D</c>.
/// </description></item>
/// <item><description>
/// <c>N</c>: the number of columns in matrix <c>B</c>, matrix <c>C</c>, and result matrix <c>D</c>.
/// </description></item>
/// <item><description>
/// <c>K</c>: the contracting inner dimension (the number of columns in matrix <c>A</c> and rows in matrix <c>B</c>).
/// </description></item>
/// </list>
/// <para>
/// Consequently, multiplicand tile <c>A</c> has dimensions <c>M x K</c>, multiplier tile <c>B</c> has dimensions
/// <c>K x N</c>, and accumulator tiles <c>C</c> and <c>D</c> have dimensions <c>M x N</c>.
/// Shape names such as <see cref="WmmaM16N16K16"/> spell <c>M</c>, <c>N</c>, and <c>K</c> in that order, matching
/// the template arguments of CUDA's <c>nvcuda::wmma::fragment</c> (which NVIDIA documents as <c>m16n16k16</c>).
/// </para>
/// <para>
/// Shape marker structs carry no runtime data and exist purely as compile-time type tags. They must only be used as
/// the <c>TShape</c> type argument of <see cref="WmmaMatrixA{TShape, T, TLayout}"/>,
/// <see cref="WmmaMatrixB{TShape, T, TLayout}"/>, and <see cref="WmmaAccumulator{TShape, T}"/>.
/// </para>
/// <para>
/// Only the shapes declared in this namespace are WMMA shapes. A type of your own that implements this interface is an
/// ordinary type: the compiler does not treat it as a shape.
/// </para>
/// </remarks>
public interface IWmmaShape;
