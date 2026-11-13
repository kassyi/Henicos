namespace Kassyi.GSharp.Core;

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
