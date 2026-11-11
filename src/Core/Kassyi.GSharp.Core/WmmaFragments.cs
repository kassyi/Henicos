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

/// <summary>
/// Represents a <c>16 x 16 x 16</c> warp matrix tile (<c>M = 16, N = 16, K = 16</c>; matrix A is <c>16 x 16</c>,
/// matrix B is <c>16 x 16</c>, and accumulator C/D is <c>16 x 16</c>), supported for <see cref="sbyte"/> and
/// <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM16N16K16 : IWmmaShape;

/// <summary>
/// Represents a <c>32 x 8 x 16</c> warp matrix tile (<c>M = 32, N = 8, K = 16</c>; matrix A is <c>32 x 16</c>,
/// matrix B is <c>16 x 8</c>, and accumulator C/D is <c>32 x 8</c>), supported for <see cref="sbyte"/> and
/// <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM32N8K16 : IWmmaShape;

/// <summary>
/// Represents an <c>8 x 32 x 16</c> warp matrix tile (<c>M = 8, N = 32, K = 16</c>; matrix A is <c>8 x 16</c>,
/// matrix B is <c>16 x 32</c>, and accumulator C/D is <c>8 x 32</c>), supported for <see cref="sbyte"/> and
/// <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM8N32K16 : IWmmaShape;

/// <summary>
/// Represents an <c>8 x 8 x 4</c> warp matrix tile (<c>M = 8, N = 8, K = 4</c>; matrix A is <c>8 x 4</c>,
/// matrix B is <c>4 x 8</c>, and accumulator C/D is <c>8 x 8</c>), supported for <see cref="double"/> inputs
/// with <see cref="double"/> accumulators (SM 8.0+).
/// </summary>
public readonly struct WmmaM8N8K4 : IWmmaShape;

/// <summary>
/// Marker interface representing the in-memory storage layout of a <see cref="WmmaMatrixA{TShape, T, TLayout}"/> or
/// <see cref="WmmaMatrixB{TShape, T, TLayout}"/> fragment.
/// </summary>
/// <remarks>
/// Layout marker structs carry no runtime data and exist purely as compile-time type tags. They must only be used as
/// the <c>TLayout</c> type argument of <see cref="WmmaMatrixA{TShape, T, TLayout}"/> and
/// <see cref="WmmaMatrixB{TShape, T, TLayout}"/>. Only the layouts declared in this namespace are WMMA layouts; a
/// type of your own that implements this interface is an ordinary type.
/// </remarks>
public interface IWmmaLayout;

/// <summary>
/// Specifies that a warp matrix A or B tile is stored in row-major order (<c>nvcuda::wmma::row_major</c>),
/// where consecutive elements of a row are contiguous in memory.
/// </summary>
public readonly struct WmmaRowMajor : IWmmaLayout;

/// <summary>
/// Specifies that a warp matrix A or B tile is stored in column-major order (<c>nvcuda::wmma::col_major</c>),
/// where consecutive elements of a column are contiguous in memory.
/// </summary>
public readonly struct WmmaColumnMajor : IWmmaLayout;

/// <summary>
/// Specifies the memory layout when loading or storing a <see cref="WmmaAccumulator{TShape, T}"/> fragment
/// (<c>nvcuda::wmma::layout_t</c>).
/// </summary>
public enum WmmaMemoryLayout
{
    /// <summary>Consecutive elements of a row are contiguous in memory (<c>nvcuda::wmma::mem_row_major</c>).</summary>
    RowMajor = 0,

    /// <summary>Consecutive elements of a column are contiguous in memory (<c>nvcuda::wmma::mem_col_major</c>).</summary>
    ColumnMajor = 1
}

/// <summary>
/// Represents an opaque warp-distributed Tensor Core fragment for multiplicand matrix A of dimension <c>M x K</c>
/// (<c>nvcuda::wmma::fragment&lt;matrix_a, ...&gt;</c>).
/// </summary>
/// <typeparam name="TShape">The tile dimensions (<see cref="WmmaM16N16K16"/>, <see cref="WmmaM32N8K16"/>, <see cref="WmmaM8N32K16"/>, or <see cref="WmmaM8N8K4"/>).</typeparam>
/// <typeparam name="T">The matrix element type (<see cref="sbyte"/>, <see cref="byte"/>, or <see cref="double"/>).</typeparam>
/// <typeparam name="TLayout">The in-memory layout (<see cref="WmmaRowMajor"/> or <see cref="WmmaColumnMajor"/>).</typeparam>
/// <remarks>
/// Fragment elements are distributed across the 32 threads of a warp in an architecture-dependent layout.
/// Declare fragments only as local variables in GPU kernels or device functions and manipulate them through <see cref="Wmma"/>.
/// </remarks>
public struct WmmaMatrixA<TShape, T, TLayout>
    where TShape : struct, IWmmaShape
    where T : unmanaged
    where TLayout : struct, IWmmaLayout;

/// <summary>
/// Represents an opaque warp-distributed Tensor Core fragment for multiplier matrix B of dimension <c>K x N</c>
/// (<c>nvcuda::wmma::fragment&lt;matrix_b, ...&gt;</c>).
/// </summary>
/// <typeparam name="TShape">The tile dimensions (<see cref="WmmaM16N16K16"/>, <see cref="WmmaM32N8K16"/>, <see cref="WmmaM8N32K16"/>, or <see cref="WmmaM8N8K4"/>).</typeparam>
/// <typeparam name="T">The matrix element type (<see cref="sbyte"/>, <see cref="byte"/>, or <see cref="double"/>).</typeparam>
/// <typeparam name="TLayout">The in-memory layout (<see cref="WmmaRowMajor"/> or <see cref="WmmaColumnMajor"/>).</typeparam>
/// <remarks>
/// Fragment elements are distributed across the 32 threads of a warp in an architecture-dependent layout.
/// Declare fragments only as local variables in GPU kernels or device functions and manipulate them through <see cref="Wmma"/>.
/// </remarks>
public struct WmmaMatrixB<TShape, T, TLayout>
    where TShape : struct, IWmmaShape
    where T : unmanaged
    where TLayout : struct, IWmmaLayout;

/// <summary>
/// Represents an opaque warp-distributed Tensor Core accumulator fragment for matrices C and D of dimension
/// <c>M x N</c>
/// (<c>nvcuda::wmma::fragment&lt;accumulator, ...&gt;</c>).
/// </summary>
/// <typeparam name="TShape">The tile dimensions (<see cref="WmmaM16N16K16"/>, <see cref="WmmaM32N8K16"/>, <see cref="WmmaM8N32K16"/>, or <see cref="WmmaM8N8K4"/>).</typeparam>
/// <typeparam name="T">The accumulator element type (<see cref="int"/> or <see cref="double"/>).</typeparam>
/// <remarks>
/// Fragment elements are distributed across the 32 threads of a warp in an architecture-dependent layout.
/// Declare fragments only as local variables in GPU kernels or device functions and manipulate them through <see cref="Wmma"/>.
/// </remarks>
public struct WmmaAccumulator<TShape, T>
    where TShape : struct, IWmmaShape
    where T : unmanaged;
