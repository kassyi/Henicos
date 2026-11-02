#nullable enable
using System;
using System.Collections.Generic;

namespace Kassyi.GSharp.Tests.Shared.Differential
{
    /// <summary>
    /// Standard factories and launch-shape matrices for differential and metamorphic shape-invariance tests.
    /// </summary>
    public static class LaunchShapes
    {
        /// <summary>Creates a 1D launch shape for the specified number of rows and block size.</summary>
        public static LaunchShape Linear(int rows, uint blockX = 64) => LaunchShape.ForRows(rows, blockX);

        /// <summary>Creates an explicit 1D grid and block launch shape.</summary>
        public static LaunchShape Grid1D(uint gridX, uint blockX, int? rows = null) =>
            new(gridX, 1, 1, blockX, 1, 1, rows ?? checked((int)((ulong)gridX * blockX)));

        /// <summary>Creates a 1D launch shape for the specified row count and block size.</summary>
        public static LaunchShape Grid1D(int rows, uint blockX = 32) => LaunchShape.ForRows(rows, blockX);

        /// <summary>
        /// Creates a non-degenerate 2D grid and block launch shape where <c>GridY &gt;= 2</c> even for small row counts such as 64 or 128.
        /// </summary>
        public static LaunchShape TwoDimensional(int rows, uint blockX = 8, uint blockY = 4)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            ArgumentOutOfRangeException.ThrowIfZero(blockX);
            ArgumentOutOfRangeException.ThrowIfZero(blockY);
            uint threadsPerBlock = checked(blockX * blockY);
            uint totalBlocks = Math.Max(2u, ((uint)rows + threadsPerBlock - 1) / threadsPerBlock);
            uint gridX = Math.Max(1u, (uint)Math.Ceiling(Math.Sqrt(totalBlocks)));
            uint gridY = Math.Max(2u, (totalBlocks + gridX - 1) / gridX);
            return new LaunchShape(gridX, gridY, 1, blockX, blockY, 1, rows);
        }

        /// <summary>
        /// Creates a non-degenerate 3D grid and block launch shape where <c>GridY &gt;= 2</c> and <c>GridZ &gt;= 2</c> even for 64 or 128 rows.
        /// </summary>
        public static LaunchShape ThreeDimensional(int rows, uint blockX = 4, uint blockY = 2, uint blockZ = 2)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            ArgumentOutOfRangeException.ThrowIfZero(blockX);
            ArgumentOutOfRangeException.ThrowIfZero(blockY);
            ArgumentOutOfRangeException.ThrowIfZero(blockZ);
            uint threadsPerBlock = checked(blockX * blockY * blockZ);
            uint totalBlocks = Math.Max(4u, ((uint)rows + threadsPerBlock - 1) / threadsPerBlock);
            uint gridX = Math.Max(1u, (uint)Math.Ceiling(Math.Cbrt(totalBlocks)));
            uint gridY = Math.Max(2u, (uint)Math.Ceiling(Math.Sqrt((totalBlocks + gridX - 1) / gridX)));
            uint gridZ = Math.Max(2u, (totalBlocks + (gridX * gridY) - 1) / (gridX * gridY));
            return new LaunchShape(gridX, gridY, gridZ, blockX, blockY, blockZ, rows);
        }

        /// <summary>Creates a 1D launch shape with a non-multiple-of-32 block width to stress partial-warp boundaries.</summary>
        public static LaunchShape IrregularBlock(int rows, uint blockX = 17) => LaunchShape.ForRows(rows, blockX);

        /// <summary>Creates a partial-warp 1D launch shape (default: 74 rows with 37 threads per block).</summary>
        public static LaunchShape PartialWarp(int rows = 74, uint blockX = 37) => LaunchShape.ForRows(rows, blockX);

        /// <summary>Creates a single-thread launch shape (1x1x1 grid, 1x1x1 block, 1 row).</summary>
        public static LaunchShape SingleThread() => new(1, 1, 1, 1, 1, 1, 1);

        /// <summary>Creates a single-thread launch shape with a specified row count.</summary>
        public static LaunchShape SingleThread(int rows) => new(1, 1, 1, 1, 1, 1, rows);

        /// <summary>Returns the default single-element 1D launch shape list.</summary>
        public static IReadOnlyList<LaunchShape> Default(int rows = 64) =>
        [
            Linear(rows, 32),
        ];

        /// <summary>Returns 1D launch shapes whose block sizes are not multiples of the 32-lane warp size.</summary>
        public static IReadOnlyList<LaunchShape> NonMultipleOfWarp(int rows = 64)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            return
            [
                Linear(rows, 1),
                Linear(rows, 7),
                Linear(rows, 17),
                Linear(rows, 31),
                Linear(rows, 33),
                Linear(rows, 63),
                Linear(rows, 65),
            ];
        }

        /// <summary>
        /// Returns the 8-shape 1D matrix covering sub-warp, prime/odd block sizes, multi-block, and idle-warp configurations.
        /// </summary>
        public static IReadOnlyList<LaunchShape> OneDimensionalMatrix(int rows = 64)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            return
            [
                Linear(rows, 1),
                Linear(rows, 7),
                Linear(rows, 31),
                Linear(rows, 32),
                Linear(rows, 33),
                Linear(rows, 63),
                Linear(rows, 65),
                Linear(rows, 128),
            ];
        }

        /// <summary>Returns axis-isolated shapes where only the Y axis or only the Z axis is greater than 1.</summary>
        public static IReadOnlyList<LaunchShape> AxisIsolated(int rows = 64)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            return
            [
                new(1, (uint)((rows + 7) / 8), 1, 1, 8, 1, rows),
                new(1, 1, (uint)((rows + 7) / 8), 1, 1, 8, rows),
            ];
        }

        /// <summary>Returns multi-dimensional shapes whose X block dimension is not a multiple of 32, forcing warps across Y and Z slices.</summary>
        public static IReadOnlyList<LaunchShape> CrossAxisWarp(int rows = 64)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            return
            [
                TwoDimensional(rows, blockX: 7, blockY: 5),
                ThreeDimensional(rows, blockX: 3, blockY: 3, blockZ: 5),
                ThreeDimensional(rows, blockX: 7, blockY: 5, blockZ: 2),
            ];
        }

        /// <summary>Returns the standard 6-shape matrix (1D, irregular 1D, non-degenerate 2D, non-multiple-of-32 2D, and non-degenerate 3D).</summary>
        public static IReadOnlyList<LaunchShape> StandardMatrix(int rows = 64)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            return
            [
                Linear(rows, 64),
                Linear(rows, 32),
                IrregularBlock(rows, 17),
                TwoDimensional(rows, 8, 4),
                TwoDimensional(rows, 7, 5),
                ThreeDimensional(rows, 4, 2, 2),
            ];
        }

        /// <summary>Returns the full 14-shape metamorphic matrix including the hardware maximum block size (1024).</summary>
        public static IReadOnlyList<LaunchShape> FullMetamorphicMatrix(int rows = 64)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rows);
            return
            [
                ..OneDimensionalMatrix(rows),
                Linear(rows, 1024),
                ..AxisIsolated(rows),
                ..CrossAxisWarp(rows),
            ];
        }
    }
}

namespace Kassyi.GSharp.Tests.Shared.Differential.Harness
{
}
