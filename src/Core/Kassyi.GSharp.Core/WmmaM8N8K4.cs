namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents an <c>8 x 8 x 4</c> warp matrix tile (<c>M = 8, N = 8, K = 4</c>; matrix A is <c>8 x 4</c>,
/// matrix B is <c>4 x 8</c>, and accumulator C/D is <c>8 x 8</c>), supported for <see cref="double"/> inputs
/// with <see cref="double"/> accumulators (SM 8.0+).
/// </summary>
public readonly struct WmmaM8N8K4 : IWmmaShape;
