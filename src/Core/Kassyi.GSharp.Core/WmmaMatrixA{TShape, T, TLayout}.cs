namespace Kassyi.GSharp.Core;

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
