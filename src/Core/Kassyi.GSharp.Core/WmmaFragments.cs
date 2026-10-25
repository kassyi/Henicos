namespace Kassyi.GSharp.Core;

/// <summary>
/// Marker interface representing the tile dimensions (<c>m x n x k</c>) of a Tensor Core warp matrix operation (<see cref="Wmma"/>).
/// </summary>
/// <remarks>
/// Shape marker structs are compile-time type tags and must only be used as <c>TShape</c> type arguments of
/// <see cref="WmmaMatrixA{TShape, T, TLayout}"/>, <see cref="WmmaMatrixB{TShape, T, TLayout}"/>, and
/// <see cref="WmmaAccumulator{TShape, T}"/>.
/// </remarks>
public interface IWmmaShape;

/// <summary>
/// Represents a <c>16 x 16 x 16</c> warp matrix tile (<c>m = 16, n = 16, k = 16</c>), supported for
/// <see cref="sbyte"/> and <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM16N16K16 : IWmmaShape;

/// <summary>
/// Represents a <c>32 x 8 x 16</c> warp matrix tile (<c>m = 32, n = 8, k = 16</c>), supported for
/// <see cref="sbyte"/> and <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM32N8K16 : IWmmaShape;

/// <summary>
/// Represents an <c>8 x 32 x 16</c> warp matrix tile (<c>m = 8, n = 32, k = 16</c>), supported for
/// <see cref="sbyte"/> and <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM8N32K16 : IWmmaShape;

/// <summary>
/// Represents an <c>8 x 8 x 4</c> warp matrix tile (<c>m = 8, n = 8, k = 4</c>), supported for
/// <see cref="double"/> inputs with <see cref="double"/> accumulators (SM 8.0+).
/// </summary>
public readonly struct WmmaM8N8K4 : IWmmaShape;

/// <summary>
/// Marker interface representing the in-memory storage layout of a <see cref="WmmaMatrixA{TShape, T, TLayout}"/> or
/// <see cref="WmmaMatrixB{TShape, T, TLayout}"/> fragment.
/// </summary>
/// <remarks>
/// Layout marker structs are compile-time type tags and must only be used as <c>TLayout</c> type arguments of
/// <see cref="WmmaMatrixA{TShape, T, TLayout}"/> and <see cref="WmmaMatrixB{TShape, T, TLayout}"/>.
/// </remarks>
public interface IWmmaLayout;

/// <summary>
/// Specifies that a warp matrix A or B tile is stored in row-major order (<c>nvcuda::wmma::row_major</c>).
/// </summary>
public readonly struct WmmaRowMajor : IWmmaLayout;

/// <summary>
/// Specifies that a warp matrix A or B tile is stored in column-major order (<c>nvcuda::wmma::col_major</c>).
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
/// Represents an opaque warp-distributed Tensor Core fragment for multiplicand matrix A (<c>nvcuda::wmma::fragment&lt;matrix_a, ...&gt;</c>).
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
/// Represents an opaque warp-distributed Tensor Core fragment for multiplicand matrix B (<c>nvcuda::wmma::fragment&lt;matrix_b, ...&gt;</c>).
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
/// Represents an opaque warp-distributed Tensor Core accumulator fragment for matrices C and D (<c>nvcuda::wmma::fragment&lt;accumulator, ...&gt;</c>).
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
