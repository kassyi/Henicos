namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents a <c>32 x 8 x 16</c> warp matrix tile (<c>M = 32, N = 8, K = 16</c>; matrix A is <c>32 x 16</c>,
/// matrix B is <c>16 x 8</c>, and accumulator C/D is <c>32 x 8</c>), supported for <see cref="sbyte"/> and
/// <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM32N8K16 : IWmmaShape;
