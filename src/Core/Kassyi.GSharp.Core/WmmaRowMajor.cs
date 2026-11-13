namespace Kassyi.GSharp.Core;

/// <summary>
/// Specifies that a warp matrix A or B tile is stored in row-major order (<c>nvcuda::wmma::row_major</c>),
/// where consecutive elements of a row are contiguous in memory.
/// </summary>
public readonly struct WmmaRowMajor : IWmmaLayout;
