// ReSharper disable All

using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.Math;

namespace DebugReduc;

public struct SpecTypes
{
    public bool BooleanValue;
    public byte ByteValue;
    public sbyte SByteValue;
    public short ShortValue;
    public ushort UShortValue;
    public int IntValue;
    public uint UIntValue;
    public long LongValue;
    public ulong ULongValue;
    public float FloatValue;
    public double DoubleValue;
}

public static class Intrinsics
{
    public static void InlinePtx(string ptx)
    {
    }
}

public struct PhysicsVector
{
    public float X;
    public float Y;
    public float Z;
}

public record struct Point(float X, float Y);

public struct NestedPhysicsState
{
    public PhysicsVector Position;
    public PhysicsVector Velocity;
    public ulong StatusFlag;
}

public static partial class ComprehensiveSpecKernel
{
    [GpuDeviceFunctionAttribute]
    [GpuInline]
    public static float ComputeMagnitude(PhysicsVector v)
    {
        return (float)Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
    }

    [GpuDeviceFunction]
    [GpuNoInline]
    public static bool IsPositive(float value)
    {
        return value > 0;
    }

    [System.Runtime.CompilerServices.InlineArray(8)]
    public struct Vector8
    {
        private uint _element0;
    }

    public unsafe struct PhysicsState
    {
        public const uint MaxParticles = 1000;

        public fixed uint Buffer[16];
        public ulong Flags;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Has intentional diagnostics errors")]
    public static void ProcessPhysics(
        PhysicsVector[] particles,
        ref float totalMagnitude,
        out bool successFlag,
        SpecTypes largeStructVal, // Triggers GSH1002 (>= 32 bytes passed by value)
        int particleCount,
        [Constant] int unrollFactor)
    {
        successFlag = false;

        const float Gravity = 9.8f; // Triggers const evaluation in C++

        // 1. All Intrinsics Indexing
        int tx = ThreadIndex.X;
        int ty = ThreadIndex.Y;
        int tz = ThreadIndex.Z;

        int bx = BlockIndex.X;
        int by = BlockIndex.Y;
        int bz = BlockIndex.Z;

        int bdimX = BlockDimension.X;
        int bdimY = BlockDimension.Y;
        int bdimZ = BlockDimension.Z;

        int gdimX = GridDimension.X;
        int gdimY = GridDimension.Y;
        int gdimZ = GridDimension.Z;

        int tid = tx + bx * bdimX;

        // 2. Shared Memory (Static & Dynamic)
        SharedMemory<int> staticCache = new SharedMemory<int>(10000); // Triggers GSH1001 (>32768 bytes)
        SharedMemory<float> dynamicCache = new SharedMemory<float>(particleCount);
        SharedMemory<int> targetTypedCache = new(256); // Target-typed new support

        if (tid < particleCount)
        {
            // 3. Custom Struct extraction & Array pointer dereferencing
            PhysicsVector p = particles[tid];
            SpecTypes types = largeStructVal; // Proves compilation of all primitive types

            // 3.5 Record struct and var keyword support
            var origin = new Point(0, 0);
            var distance = origin.X + p.X;

            // 3.6 Nested Structs
            var nested = new NestedPhysicsState
            {
                Position = p,
                Velocity = p,
                StatusFlag = 1
            };

            // 4. All Math Mappings and Device Method invocation
            float mag = ComputeMagnitude(p);
            mag = (float)System.Math.Abs(mag);

            // 5. Automatic Loop Unrolling via Constant (for)
            for (int i = 0; i < unrollFactor; i++)
            {
                if (i == 1) continue;
                mag = (float)System.Math.Max(mag, p.X * i + Gravity);
            }

            if (!IsPositive(mag))
            {
                mag = 0;
            }

            // 6. Other Loop Syntaxes
            int counter = 0;
            while (counter < 2)
            {
                mag = (float)System.Math.Min(mag, mag + 1);
                counter++;
            }

            do
            {
                counter--;
            } while (counter > 0);

            // 7. Switch statement
            switch (tx)
            {
                case 0:
                    mag += 1;
                    break;
                default:
                    mag += 0;
                    break;
            }

            // 8. Memory writes
            staticCache[tx] = tid;
            dynamicCache[tx] = mag;

            // 9. Synchronization
            Gpu.SyncThreads();

            // 10. Atomics and Out parameter
            if (tx == 0)
            {
                Gpu.AtomicAdd(ref totalMagnitude, dynamicCache[0]);
                successFlag = true;
            }

            // 11. Phase 2 Features: InlineArray, unsafe fixed, AddWithCarry
            Vector8 inlineVec = new Vector8();
            inlineVec[0] = 123;
            inlineVec[1] = 456;

            unsafe
            {
                PhysicsState state = new PhysicsState();

                // Pointer operators and sizeof
                PhysicsState* pState = &state;
                // int stateSize = sizeof(PhysicsState); // GSH0003

                pState->Buffer[0] = inlineVec[0];
                (*pState).Buffer[1] = inlineVec[1];

                ulong carry = 0;
                uint sum = Gpu.AddWithCarry(pState->Buffer[0], pState->Buffer[1], ref carry);

                long borrow = 0;
                uint diff = Gpu.SubWithBorrow(sum, pState->Buffer[1], ref borrow);

                if (sum > diff)
                {
                    pState->Flags = 1;
                }
            }

            // 11.6 Operators (Ternary, Bitwise, Logical, else if)
            bool condition1 = tx > 0;
            bool condition2 = ty == 0;
            if (condition1 && condition2) // Logical short-circuit
            {
                int bitwise = (tx << 2) | (ty >> 1) ^ 0xFF;
                bitwise &= ~1; // Bitwise assignment and NOT

                // mag = (bitwise > 0) ? mag * 2.0f : mag / 2.0f; // Ternary conditional - GSH0003
            }
            else if (condition1 || !condition2)
            {
                mag += 1.5f;
            }

            // 11.5 Inline PTX
            Intrinsics.InlinePtx("/* test ptx inline */");

            // 12. Early return
            if (ty > 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Demonstrates the host-side AST construction for GpuBigInt.
    /// This method is ignored by the GSharp Transpiler because it lacks [GpuKernel] or [GpuDeviceFunction].
    /// </summary>
    public static void HostSideGpuBigIntDemo()
    {
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

        using var a = new GpuUIntBigInteger(context, 1024);
        using var b = new GpuUIntBigInteger(context, 1024);
        using var result = new GpuUIntBigInteger(context, 1024);

        // AST Construction: evaluated lazily and dispatched to GPU
        result.Value = a + b;
        b.Value = result - a;
    }

    [GpuKernel(Cooperative = true)]
    public static void CooperativePhysicsProcess(int[] data)
    {
        int tid = ThreadIndex.X;
        data[tid] = tid;
        Gpu.SyncGrid();
    }
}
