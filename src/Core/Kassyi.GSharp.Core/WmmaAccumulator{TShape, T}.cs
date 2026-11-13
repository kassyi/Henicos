namespace Kassyi.GSharp.Core;

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
