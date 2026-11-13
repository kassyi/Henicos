namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents an <c>8 x 32 x 16</c> warp matrix tile (<c>M = 8, N = 32, K = 16</c>; matrix A is <c>8 x 16</c>,
/// matrix B is <c>16 x 32</c>, and accumulator C/D is <c>8 x 32</c>), supported for <see cref="sbyte"/> and
/// <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM8N32K16 : IWmmaShape;
