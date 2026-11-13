namespace Kassyi.GSharp.Core;

/// <summary>
/// Specifies that a warp matrix A or B tile is stored in column-major order (<c>nvcuda::wmma::col_major</c>),
/// where consecutive elements of a column are contiguous in memory.
/// </summary>
public readonly struct WmmaColumnMajor : IWmmaLayout;
