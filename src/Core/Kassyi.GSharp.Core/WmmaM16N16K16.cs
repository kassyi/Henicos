namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents a <c>16 x 16 x 16</c> warp matrix tile (<c>M = 16, N = 16, K = 16</c>; matrix A is <c>16 x 16</c>,
/// matrix B is <c>16 x 16</c>, and accumulator C/D is <c>16 x 16</c>), supported for <see cref="sbyte"/> and
/// <see cref="byte"/> inputs with <see cref="int"/> accumulators (SM 7.2+).
/// </summary>
public readonly struct WmmaM16N16K16 : IWmmaShape;
