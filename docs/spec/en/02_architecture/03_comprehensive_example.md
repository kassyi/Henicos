---
title: "8. Comprehensive Example"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "This document provides a 'Rosetta Stone' example that validates all of the specifications detailed in this repository. It demonstrates how a single..."
---

# 8. Comprehensive Example

> [!WARNING]
> **Compiler Validation Reference Code**
> This file is **not** a beginner tutorial or a getting-started guide. It is a comprehensive "Rosetta Stone" test-case designed to demonstrate how the G# transpiler converts all supported C# features into C++. It contains intentional edge-cases and hundreds of lines of code, serving primarily as a compiler validation reference.

This document provides a "Rosetta Stone" example that validates all of the specifications detailed in this repository. It demonstrates how a single G# C# file utilizes custom `struct` definitions, `[GpuKernel]`, `[GpuDeviceFunction]`, `[Constant]`, intrinsic math functions, static/dynamic shared memory, atomics, thread synchronization, and implicit array marshaling.

## 8.1 The G# Source Code

// Execution Context: G# Kernel (GPU)

<!-- BEGIN CSHARP -->

```csharp
// ReSharper disable All

using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.GpuMath;

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
        SpecTypes largeStructVal, // 48 bytes by value; within the GSH0064 parameter limit.
        int particleCount,
        [GpuConstexpr(4)] int unrollFactor)
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
        SharedMemory<int> staticCache = new SharedMemory<int>(10000); // Triggers GSH0013 (>32768 bytes)
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
            mag = (float)Math.Abs(mag);

            // 5. Automatic Loop Unrolling via Constant (for)
            for (int i = 0; i < unrollFactor; i++)
            {
                if (i == 1) continue;
                mag = (float)Math.Max(mag, p.X * i + Gravity);
            }

            if (!IsPositive(mag))
            {
                mag = 0;
            }

            // 6. Other Loop Syntaxes
            int counter = 0;
            while (counter < 2)
            {
                mag = (float)Math.Min(mag, mag + 1);
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

                state.Buffer[0] = inlineVec[0];
                state.Buffer[1] = inlineVec[1];

                ulong carry = 0;
                uint sum = Gpu.AddWithCarry(state.Buffer[0], state.Buffer[1], ref carry);

                long borrow = 0;
                uint diff = Gpu.SubWithBorrow(sum, state.Buffer[1], ref borrow);

                if (sum > diff)
                {
                    state.Flags = 1;
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
            Gpu.InlinePtx("/* test ptx inline */");

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
        using var context = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

        using var a = new GpuBigUInt(context, 1024);
        using var b = new GpuBigUInt(context, 1024);
        using var result = new GpuBigUInt(context, 1024);

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
```

<!-- END CSHARP -->

## 8.2 Build Output (Diagnostics)

When building this code, MSBuild natively catches the strict constraints via the `Kassyi.Generators.GSharp` analyzers:

```
ComprehensiveSpecKernel.cu.cs(107,63): warning GSH0013: The kernel allocates 40000 bytes of static shared memory, which exceeds the recommended limit of 32768 bytes. This may cause kernel launch failure or reduce occupancy.
```

## 8.3 The Transpiled CUDA C++ Output

// Execution Context: Transpiled CUDA C++ (Internal)

The generator emits one C# file per kernel class. The CUDA C++ for each kernel is embedded in a `{Kernel}_Source` string constant and compiled together with the shared prelude and device code (`{Kernel}_FullSource`). The rest of the file is the host side: native shadow structs (`AotContexts`), the managed-array launcher `Launch{Kernel}`, and the raw-pointer launcher `Launch{Kernel}Ptr`.

Inside the C++, every user identifier is renamed from its symbol: parameters and locals become `__gsharp_id_{n}_{name}`, and fields of structs that G# defines become `__gsharp_mb_{name}`. The renaming keeps C# names that are reserved in C++ or CUDA from breaking the build. See [Identifier Naming](01_aot_transpiler.md#126-identifier-naming-cppnameresolver).

The listing below is the CUDA snapshot of this file (`tests/Compiler/Kassyi.GSharp.Tests.Cuda/Snapshots/Snap.TestComprehensiveSpecKern_*_Cuda#ComprehensiveSpecKernel.g.verified.cs`). Regenerate §8.1 and §8.3 with `node scripts/sync-comprehensive-spec.js`.

<!-- BEGIN CPP -->

```csharp
//HintName: DebugReduc__ComprehensiveSpecKernel.g.cs
// <auto-generated/>
#nullable enable annotations
using System;
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
#pragma warning disable RS1016
#pragma warning disable RS1034
using System.Text;
using System.Runtime.InteropServices;
using global::Kassyi.GSharp.Core;
using global::Kassyi.GSharp.Runtime.Interop;
using static global::Kassyi.GSharp.Core.ManagedGpuContext;
using global::Kassyi.ZeroAlloc.Linq;
#pragma warning disable CS0162, CS0164, CS0219, CS0649, CS1718
namespace DebugReduc
{
public partial class ComprehensiveSpecKernel
{
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static partial class AotContexts
{
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = 48)]
public unsafe partial struct SpecTypes
{
public const string DebugName_BooleanValue = "global::System.Boolean";
[global::System.Runtime.InteropServices.FieldOffset(0)]
public global::System.Boolean BooleanValue;
public const string DebugName_ByteValue = "global::System.Byte";
[global::System.Runtime.InteropServices.FieldOffset(1)]
public global::System.Byte ByteValue;
public const string DebugName_SByteValue = "global::System.SByte";
[global::System.Runtime.InteropServices.FieldOffset(2)]
public global::System.SByte SByteValue;
public const string DebugName_ShortValue = "global::System.Int16";
[global::System.Runtime.InteropServices.FieldOffset(4)]
public global::System.Int16 ShortValue;
public const string DebugName_UShortValue = "global::System.UInt16";
[global::System.Runtime.InteropServices.FieldOffset(6)]
public global::System.UInt16 UShortValue;
public const string DebugName_IntValue = "global::System.Int32";
[global::System.Runtime.InteropServices.FieldOffset(8)]
public global::System.Int32 IntValue;
public const string DebugName_UIntValue = "global::System.UInt32";
[global::System.Runtime.InteropServices.FieldOffset(12)]
public global::System.UInt32 UIntValue;
public const string DebugName_LongValue = "global::System.Int64";
[global::System.Runtime.InteropServices.FieldOffset(16)]
public global::System.Int64 LongValue;
public const string DebugName_ULongValue = "global::System.UInt64";
[global::System.Runtime.InteropServices.FieldOffset(24)]
public global::System.UInt64 ULongValue;
public const string DebugName_FloatValue = "global::System.Single";
[global::System.Runtime.InteropServices.FieldOffset(32)]
public global::System.Single FloatValue;
public const string DebugName_DoubleValue = "global::System.Double";
[global::System.Runtime.InteropServices.FieldOffset(40)]
public global::System.Double DoubleValue;
}
public static unsafe void Marshal_SpecTypes(ref global::DebugReduc.SpecTypes host, ref AotContexts.SpecTypes native, bool isDevicePtr, global::Kassyi.GSharp.Runtime.Interop.DeviceArrayRegions? deviceArrays, global::System.IntPtr hStream = default)
{
native = global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, AotContexts.SpecTypes>(ref host);
}
public static unsafe void Unmarshal_SpecTypes(ref global::DebugReduc.SpecTypes host, ref AotContexts.SpecTypes native, bool isDevicePtr, global::System.IntPtr hStream = default)
{
host = global::System.Runtime.CompilerServices.Unsafe.As<AotContexts.SpecTypes, global::DebugReduc.SpecTypes>(ref native);
}
#pragma warning restore CS8500
}
public const string ProcessPhysics_Source = @"#ifndef __GSHARP_STRUCT___gsharp_ty_DebugReduc__PhysicsVector__
#define __GSHARP_STRUCT___gsharp_ty_DebugReduc__PhysicsVector__
struct __gsharp_ty_DebugReduc__PhysicsVector
{
float __gsharp_mb_X;
float __gsharp_mb_Y;
float __gsharp_mb_Z;
}
;
static_assert(sizeof(__gsharp_ty_DebugReduc__PhysicsVector)==12,""DebugReduc.PhysicsVector: size"");
static_assert(alignof(__gsharp_ty_DebugReduc__PhysicsVector)==4,""DebugReduc.PhysicsVector: alignment"");
static_assert(offsetof(__gsharp_ty_DebugReduc__PhysicsVector,__gsharp_mb_X)==0,""DebugReduc.PhysicsVector.X: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__PhysicsVector,__gsharp_mb_Y)==4,""DebugReduc.PhysicsVector.Y: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__PhysicsVector,__gsharp_mb_Z)==8,""DebugReduc.PhysicsVector.Z: offset"");
#endif
#ifndef __GSHARP_STRUCT___gsharp_ty_DebugReduc__SpecTypes__
#define __GSHARP_STRUCT___gsharp_ty_DebugReduc__SpecTypes__
struct __gsharp_ty_DebugReduc__SpecTypes
{
bool __gsharp_mb_BooleanValue;
unsigned char __gsharp_mb_ByteValue;
signed char __gsharp_mb_SByteValue;
short __gsharp_mb_ShortValue;
unsigned short __gsharp_mb_UShortValue;
int __gsharp_mb_IntValue;
unsigned int __gsharp_mb_UIntValue;
long long __gsharp_mb_LongValue;
unsigned long long __gsharp_mb_ULongValue;
float __gsharp_mb_FloatValue;
double __gsharp_mb_DoubleValue;
}
;
static_assert(sizeof(__gsharp_ty_DebugReduc__SpecTypes)==48,""DebugReduc.SpecTypes: size"");
static_assert(alignof(__gsharp_ty_DebugReduc__SpecTypes)==8,""DebugReduc.SpecTypes: alignment"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_BooleanValue)==0,""DebugReduc.SpecTypes.BooleanValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_ByteValue)==1,""DebugReduc.SpecTypes.ByteValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_SByteValue)==2,""DebugReduc.SpecTypes.SByteValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_ShortValue)==4,""DebugReduc.SpecTypes.ShortValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_UShortValue)==6,""DebugReduc.SpecTypes.UShortValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_IntValue)==8,""DebugReduc.SpecTypes.IntValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_UIntValue)==12,""DebugReduc.SpecTypes.UIntValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_LongValue)==16,""DebugReduc.SpecTypes.LongValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_ULongValue)==24,""DebugReduc.SpecTypes.ULongValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_FloatValue)==32,""DebugReduc.SpecTypes.FloatValue: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__SpecTypes,__gsharp_mb_DoubleValue)==40,""DebugReduc.SpecTypes.DoubleValue: offset"");
#endif
#ifndef __GSHARP_STRUCT___gsharp_ty_DebugReduc__Point__
#define __GSHARP_STRUCT___gsharp_ty_DebugReduc__Point__
struct __gsharp_ty_DebugReduc__Point
{
float __gsharp_mb_X;
float __gsharp_mb_Y;
}
;
static_assert(sizeof(__gsharp_ty_DebugReduc__Point)==8,""DebugReduc.Point: size"");
static_assert(alignof(__gsharp_ty_DebugReduc__Point)==4,""DebugReduc.Point: alignment"");
static_assert(offsetof(__gsharp_ty_DebugReduc__Point,__gsharp_mb_X)==0,""DebugReduc.Point.X: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__Point,__gsharp_mb_Y)==4,""DebugReduc.Point.Y: offset"");
__device__ inline __gsharp_ty_DebugReduc__Point __gsharp_ctor_DebugReduc__Point(float __gsharp_id_0_X, float __gsharp_id_1_Y) {
__gsharp_ty_DebugReduc__Point __gsharp_self{};
__gsharp_self.__gsharp_mb_X = __gsharp_id_0_X;
__gsharp_self.__gsharp_mb_Y = __gsharp_id_1_Y;
return __gsharp_self;
}
#endif
#ifndef __GSHARP_STRUCT___gsharp_ty_DebugReduc__NestedPhysicsState__
#define __GSHARP_STRUCT___gsharp_ty_DebugReduc__NestedPhysicsState__
struct __gsharp_ty_DebugReduc__NestedPhysicsState
{
__gsharp_ty_DebugReduc__PhysicsVector __gsharp_mb_Position;
__gsharp_ty_DebugReduc__PhysicsVector __gsharp_mb_Velocity;
unsigned long long __gsharp_mb_StatusFlag;
}
;
static_assert(sizeof(__gsharp_ty_DebugReduc__NestedPhysicsState)==32,""DebugReduc.NestedPhysicsState: size"");
static_assert(alignof(__gsharp_ty_DebugReduc__NestedPhysicsState)==8,""DebugReduc.NestedPhysicsState: alignment"");
static_assert(offsetof(__gsharp_ty_DebugReduc__NestedPhysicsState,__gsharp_mb_Position)==0,""DebugReduc.NestedPhysicsState.Position: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__NestedPhysicsState,__gsharp_mb_Velocity)==12,""DebugReduc.NestedPhysicsState.Velocity: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__NestedPhysicsState,__gsharp_mb_StatusFlag)==24,""DebugReduc.NestedPhysicsState.StatusFlag: offset"");
#endif
#ifndef __GSHARP_STRUCT___gsharp_ty_DebugReduc__ComprehensiveSpecKernel__Vector8__
#define __GSHARP_STRUCT___gsharp_ty_DebugReduc__ComprehensiveSpecKernel__Vector8__
struct __gsharp_ty_DebugReduc__ComprehensiveSpecKernel__Vector8
{
unsigned int __gsharp_mb__element0[8];
__device__ inline unsigned int& operator[](int index){
return __gsharp_mb__element0[index];
}
__device__ inline const unsigned int& operator[](int index) const
{
return __gsharp_mb__element0[index];
}
__device__ inline unsigned int* begin() { return __gsharp_mb__element0; }
__device__ inline unsigned int* end() { return __gsharp_mb__element0 + 8; }
__device__ inline const unsigned int* begin() const { return __gsharp_mb__element0; }
__device__ inline const unsigned int* end() const { return __gsharp_mb__element0 + 8; }
__device__ inline int size() const { return 8; }
}
;
static_assert(sizeof(__gsharp_ty_DebugReduc__ComprehensiveSpecKernel__Vector8)==32,""DebugReduc.ComprehensiveSpecKernel.Vector8: size"");
static_assert(alignof(__gsharp_ty_DebugReduc__ComprehensiveSpecKernel__Vector8)==4,""DebugReduc.ComprehensiveSpecKernel.Vector8: alignment"");
#endif
#ifndef __GSHARP_STRUCT___gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState__
#define __GSHARP_STRUCT___gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState__
struct __gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState
{
unsigned int __gsharp_mb_Buffer[16];
unsigned long long __gsharp_mb_Flags;
}
;
static_assert(sizeof(__gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState)==72,""DebugReduc.ComprehensiveSpecKernel.PhysicsState: size"");
static_assert(alignof(__gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState)==8,""DebugReduc.ComprehensiveSpecKernel.PhysicsState: alignment"");
static_assert(offsetof(__gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState,__gsharp_mb_Buffer)==0,""DebugReduc.ComprehensiveSpecKernel.PhysicsState.Buffer: offset"");
static_assert(offsetof(__gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState,__gsharp_mb_Flags)==64,""DebugReduc.ComprehensiveSpecKernel.PhysicsState.Flags: offset"");
#endif
// ProcessPhysics reaches 1 dynamic shared memory declaration
struct __gsharp_dsmem_DebugReduc__ComprehensiveSpecKernel__ProcessPhysics {
static constexpr int Slots = 1;
template <unsigned long long Key> static constexpr int Slot = Key == 0xe20e18017ad9544dull ? 0 : -1;
unsigned int cursor;
unsigned int offset[1];
unsigned int size[1];
};
extern ""C"" __global__ void __gsharp_k_DebugReduc__ComprehensiveSpecKernel__ProcessPhysics(GSharpArray<__gsharp_ty_DebugReduc__PhysicsVector> __gsharp_id_0_particles, float* __gsharp_id_1_totalMagnitude, bool* __gsharp_id_2_successFlag, __gsharp_ty_DebugReduc__SpecTypes __gsharp_id_3_largeStructVal, int __gsharp_id_4_particleCount, int __gsharp_id_5_unrollFactor){
__gsharp_dsmem_DebugReduc__ComprehensiveSpecKernel__ProcessPhysics __gsharp_dsmem_storage;
__gsharp_dsmem_DebugReduc__ComprehensiveSpecKernel__ProcessPhysics* __gsharp_dsmem = &__gsharp_dsmem_storage;
__gsharp_dsmem_init(__gsharp_dsmem);
(*__gsharp_id_2_successFlag)=false;
float __gsharp_id_6_Gravity = 9.8f;
int __gsharp_id_7_tx = (int)threadIdx.x;
int __gsharp_id_8_ty = (int)threadIdx.y;
int __gsharp_id_9_tz = (int)threadIdx.z;
int __gsharp_id_10_bx = (int)blockIdx.x;
int __gsharp_id_11_by = (int)blockIdx.y;
int __gsharp_id_12_bz = (int)blockIdx.z;
int __gsharp_id_13_bdimX = (int)blockDim.x;
int __gsharp_id_14_bdimY = (int)blockDim.y;
int __gsharp_id_15_bdimZ = (int)blockDim.z;
int __gsharp_id_16_gdimX = (int)gridDim.x;
int __gsharp_id_17_gdimY = (int)gridDim.y;
int __gsharp_id_18_gdimZ = (int)gridDim.z;
int __gsharp_id_19_tid = __gsharp_add_i32(__gsharp_id_7_tx,__gsharp_mul_i32(__gsharp_id_10_bx,__gsharp_id_13_bdimX));
__shared__ int __gsharp_id_20_staticCache[10000];
float* __gsharp_id_21_dynamicCache = (float*)__gsharp_dsmem_place<__gsharp_dsmem_DebugReduc__ComprehensiveSpecKernel__ProcessPhysics::Slot<0xe20e18017ad9544dull>>(__gsharp_dsmem, (unsigned long long)(long long)(__gsharp_id_4_particleCount) * sizeof(float), __GSHARP_DSMEM_ALIGN(float));
__shared__ int __gsharp_id_22_targetTypedCache[256];
if(__gsharp_id_19_tid<__gsharp_id_4_particleCount)
{
__gsharp_ty_DebugReduc__PhysicsVector __gsharp_id_23_p = __gsharp_id_0_particles[__gsharp_id_19_tid];
__gsharp_ty_DebugReduc__SpecTypes __gsharp_id_24_types = __gsharp_id_3_largeStructVal;
auto __gsharp_id_25_origin = __gsharp_ctor_DebugReduc__Point(0, 0);
auto __gsharp_id_26_distance = __gsharp_id_25_origin.__gsharp_mb_X+__gsharp_id_23_p.__gsharp_mb_X;
auto __gsharp_id_27_nested = ([&]() {
__gsharp_ty_DebugReduc__NestedPhysicsState __gsharp_tmp{}; __gsharp_tmp.__gsharp_mb_Position = __gsharp_id_23_p; __gsharp_tmp.__gsharp_mb_Velocity = __gsharp_id_23_p; __gsharp_tmp.__gsharp_mb_StatusFlag = 1; return __gsharp_tmp; }
());
float __gsharp_id_28_mag = __gsharp_fn_DebugReduc__ComprehensiveSpecKernel__ComputeMagnitude(__gsharp_id_23_p);
__gsharp_id_28_mag=(float)fabsf(__gsharp_id_28_mag);
for(int __gsharp_id_29_i = 0;__gsharp_id_29_i<__gsharp_id_5_unrollFactor;__gsharp_id_29_i++)
{
if(__gsharp_id_29_i==1)
{
continue;
}
__gsharp_id_28_mag=(float)__gsharp_max_f32(__gsharp_id_28_mag,__gsharp_id_23_p.__gsharp_mb_X*__gsharp_id_29_i+__gsharp_id_6_Gravity);
}
if(!__gsharp_fn_DebugReduc__ComprehensiveSpecKernel__IsPositive(__gsharp_id_28_mag))
{
__gsharp_id_28_mag=0;
}
int __gsharp_id_30_counter = 0;
while(__gsharp_id_30_counter<2)
{
__gsharp_id_28_mag=(float)__gsharp_min_f32(__gsharp_id_28_mag,__gsharp_id_28_mag+1);
__gsharp_id_30_counter=__gsharp_add_i32(__gsharp_id_30_counter,1);
}
do
{
__gsharp_id_30_counter=__gsharp_sub_i32(__gsharp_id_30_counter,1);
}
while(__gsharp_id_30_counter>0);
switch(__gsharp_id_7_tx)
{
case 0:
__gsharp_switch_1_state_0:
{
__gsharp_id_28_mag+=1;
break;
}
default:
__gsharp_switch_1_state_default:
{
__gsharp_id_28_mag+=0;
break;
}
}
__gsharp_id_20_staticCache[__gsharp_id_7_tx]=__gsharp_id_19_tid;
__gsharp_id_21_dynamicCache[__gsharp_id_7_tx]=__gsharp_id_28_mag;
__syncthreads();
if(__gsharp_id_7_tx==0)
{
atomicAdd(&(*__gsharp_id_1_totalMagnitude),__gsharp_id_21_dynamicCache[0]);
(*__gsharp_id_2_successFlag)=true;
}
__gsharp_ty_DebugReduc__ComprehensiveSpecKernel__Vector8 __gsharp_id_31_inlineVec = __gsharp_ty_DebugReduc__ComprehensiveSpecKernel__Vector8{};
__gsharp_id_31_inlineVec[0]=123;
__gsharp_id_31_inlineVec[1]=456;
{
__gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState __gsharp_id_32_state = __gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState{};
__gsharp_id_32_state.__gsharp_mb_Buffer[0]=__gsharp_id_31_inlineVec[0];
__gsharp_id_32_state.__gsharp_mb_Buffer[1]=__gsharp_id_31_inlineVec[1];
unsigned long long __gsharp_id_33_carry = 0;
unsigned int __gsharp_id_34_sum = __gsharp_add_cc(__gsharp_id_32_state.__gsharp_mb_Buffer[0],__gsharp_id_32_state.__gsharp_mb_Buffer[1],&__gsharp_id_33_carry);
long long __gsharp_id_35_borrow = 0;
unsigned int __gsharp_id_36_diff = __gsharp_sub_cc(__gsharp_id_34_sum,__gsharp_id_32_state.__gsharp_mb_Buffer[1],&__gsharp_id_35_borrow);
if(__gsharp_id_34_sum>__gsharp_id_36_diff)
{
__gsharp_id_32_state.__gsharp_mb_Flags=1;
}
}
bool __gsharp_id_37_condition1 = __gsharp_id_7_tx>0;
bool __gsharp_id_38_condition2 = __gsharp_id_8_ty==0;
if(__gsharp_id_37_condition1&&__gsharp_id_38_condition2)
{
int __gsharp_id_39_bitwise = (__gsharp_shl_i32(__gsharp_id_7_tx,2))|(__gsharp_id_8_ty>>1)^0xFF;
__gsharp_id_39_bitwise&=(-2);
}
else
{
if(__gsharp_id_37_condition1||!__gsharp_id_38_condition2)
{
__gsharp_id_28_mag+=1.5f;
}
}
/* test ptx inline */
;
if(__gsharp_id_8_ty>0)
{
return;
}
}
}
";
public static readonly string ProcessPhysics_FullSource=global::Kassyi.GSharp.Generated.GSharpSharedCudaPrelude.Source +global::Kassyi.GSharp.Generated.GSharpSharedCudaDeviceCode.Source +ProcessPhysics_Source;
private static readonly global::Kassyi.GSharp.Runtime.Aot.CudaKernelFunction __gsharp_hfunc_ProcessPhysics = new global::Kassyi.GSharp.Runtime.Aot.CudaKernelFunction("__gsharp_k_DebugReduc__ComprehensiveSpecKernel__ProcessPhysics", global::Kassyi.GSharp.Generated.GSharpModuleContext_MegaTestCompilationBase.GetFatbin);
private static bool __gsharp_layout_checked_ProcessPhysics;
/// <summary>Preloads the CUDA module and resolves the function pointer for this kernel in the current CUDA context.</summary>
public static void WarmupFunctionPointerProcessPhysics() => global::DebugReduc.ComprehensiveSpecKernel.__gsharp_hfunc_ProcessPhysics.Resolve();
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static unsafe void LaunchProcessPhysics(global::DebugReduc.PhysicsVector[] particles, ref global::System.Single totalMagnitude, out global::System.Boolean successFlag, global::DebugReduc.SpecTypes largeStructVal, global::System.Int32 particleCount, global::System.Int32 unrollFactor, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1,uint blockDimZ = 1, uint sharedMemBytes = 0, global::System.IntPtr hStream = default, global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
if (!__gsharp_layout_checked_ProcessPhysics)
{
if (global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::DebugReduc.PhysicsVector>() != 12) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowSizeMismatch("DebugReduc.PhysicsVector", 12, global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::DebugReduc.PhysicsVector>());
{
global::DebugReduc.PhysicsVector __gsharp_layout_probe = default;
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.PhysicsVector, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Single, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.X))) != 0) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.PhysicsVector", "X", 0, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.PhysicsVector, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Single, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.X))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.PhysicsVector, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Single, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.Y))) != 4) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.PhysicsVector", "Y", 4, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.PhysicsVector, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Single, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.Y))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.PhysicsVector, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Single, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.Z))) != 8) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.PhysicsVector", "Z", 8, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.PhysicsVector, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Single, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.Z))));
}
if (global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::DebugReduc.SpecTypes>() != 48) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowSizeMismatch("DebugReduc.SpecTypes", 48, global::System.Runtime.CompilerServices.Unsafe.SizeOf<global::DebugReduc.SpecTypes>());
{
global::DebugReduc.SpecTypes __gsharp_layout_probe = default;
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Boolean, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.BooleanValue))) != 0) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "BooleanValue", 0, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Boolean, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.BooleanValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Byte, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.ByteValue))) != 1) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "ByteValue", 1, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Byte, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.ByteValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.SByte, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.SByteValue))) != 2) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "SByteValue", 2, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.SByte, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.SByteValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Int16, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.ShortValue))) != 4) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "ShortValue", 4, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Int16, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.ShortValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.UInt16, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.UShortValue))) != 6) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "UShortValue", 6, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.UInt16, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.UShortValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Int32, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.IntValue))) != 8) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "IntValue", 8, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Int32, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.IntValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.UInt32, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.UIntValue))) != 12) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "UIntValue", 12, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.UInt32, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.UIntValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Int64, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.LongValue))) != 16) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "LongValue", 16, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Int64, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.LongValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.UInt64, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.ULongValue))) != 24) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "ULongValue", 24, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.UInt64, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.ULongValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Single, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.FloatValue))) != 32) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "FloatValue", 32, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Single, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.FloatValue))));
if (global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Double, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.DoubleValue))) != 40) global::Kassyi.GSharp.Runtime.Interop.LayoutGuard.ThrowFieldOffsetMismatch("DebugReduc.SpecTypes", "DoubleValue", 40, (long)global::System.Runtime.CompilerServices.Unsafe.ByteOffset(ref global::System.Runtime.CompilerServices.Unsafe.As<global::DebugReduc.SpecTypes, byte>(ref __gsharp_layout_probe), ref global::System.Runtime.CompilerServices.Unsafe.As<global::System.Double, byte>(ref global::System.Runtime.CompilerServices.Unsafe.AsRef(in __gsharp_layout_probe.DoubleValue))));
}
__gsharp_layout_checked_ProcessPhysics = true;
}
successFlag = default;
if (particles is null){
throw new global::System.ArgumentNullException(nameof(particles));
}
if (particles.Length == 0){
throw new global::System.ArgumentException("Kernel array arguments must not be empty. Declare the parameter as nullable (T[]? or Memory<T>?) to accept null or empty input.", nameof(particles));
}
using (var __gsharp_cuda_context_scope = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
{
global::System.IntPtr __gsharp_hfunc = global::DebugReduc.ComprehensiveSpecKernel.__gsharp_hfunc_ProcessPhysics.Resolve();
global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare(__gsharp_hfunc, sharedMemBytes, "ComprehensiveSpecKernel.ProcessPhysics");
global::System.IntPtr __gsharp_d_0 = global::System.IntPtr.Zero;
global::System.IntPtr __gsharp_d_1 = global::System.IntPtr.Zero;
global::System.IntPtr __gsharp_d_2 = global::System.IntPtr.Zero;
try
{
void** __gsharp_kernel_params = stackalloc void*[6];
// Marshal array: particles
int __gsharp_size_0 = checked(particles.Length * sizeof(global::DebugReduc.PhysicsVector));
global::System.IntPtr* __gsharp_proxy_0 = stackalloc global::System.IntPtr[2];
__gsharp_proxy_0[0] = global::System.IntPtr.Zero;
((int*)(__gsharp_proxy_0 + 1))[0] = particles.Length;
((int*)(__gsharp_proxy_0 + 1))[1] = 0;
if (__gsharp_size_0 > 0)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemAllocAsync(out __gsharp_d_0, (global::System.IntPtr)__gsharp_size_0, hStream));
fixed (global::DebugReduc.PhysicsVector* __gsharp_p_0 = particles)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemcpyHtoDAsync_v2(__gsharp_d_0, (global::System.IntPtr)__gsharp_p_0, (global::System.IntPtr)__gsharp_size_0, hStream));
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuStreamSynchronize(hStream));
}
__gsharp_proxy_0[0] = __gsharp_d_0;
}
__gsharp_kernel_params[0] = __gsharp_proxy_0;
// Marshal ref/out scalar: totalMagnitude
int __gsharp_size_1 = sizeof(global::System.Single);
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemAllocAsync(out __gsharp_d_1, (global::System.IntPtr)__gsharp_size_1, hStream));
// Fast zero-allocation copy to device
fixed (global::System.Single* __gsharp_p_temp_1 = &totalMagnitude)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemcpyHtoDAsync_v2(__gsharp_d_1, (global::System.IntPtr)__gsharp_p_temp_1, (global::System.IntPtr)__gsharp_size_1, hStream));
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuStreamSynchronize(hStream));
}
__gsharp_kernel_params[1] = &__gsharp_d_1;
// Marshal ref/out scalar: successFlag
int __gsharp_size_2 = sizeof(global::System.Boolean);
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemAllocAsync(out __gsharp_d_2, (global::System.IntPtr)__gsharp_size_2, hStream));
__gsharp_kernel_params[2] = &__gsharp_d_2;
// Marshal scalar struct: largeStructVal
__gsharp_kernel_params[3] = &largeStructVal;
// Marshal scalar: particleCount
__gsharp_kernel_params[4] = &particleCount;
// Marshal scalar: unrollFactor
__gsharp_kernel_params[5] = &unrollFactor;
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuLaunchKernel(
__gsharp_hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
hStream, // Stream
__gsharp_kernel_params,
global::System.IntPtr.Zero // Extra
));
// Copy back array: particles
if (__gsharp_size_0 > 0 && __gsharp_d_0 != global::System.IntPtr.Zero)
{
fixed (global::DebugReduc.PhysicsVector* __gsharp_p_0 = particles)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemcpyDtoHAsync_v2((global::System.IntPtr)__gsharp_p_0, __gsharp_d_0, (global::System.IntPtr)__gsharp_size_0, hStream));
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuStreamSynchronize(hStream));
}
}
// Fast zero-allocation copy from device for scalar: totalMagnitude
fixed (global::System.Single* __gsharp_ptr_1 = &totalMagnitude)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemcpyDtoHAsync_v2((global::System.IntPtr)__gsharp_ptr_1, __gsharp_d_1, (global::System.IntPtr)__gsharp_size_1, hStream));
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuStreamSynchronize(hStream));
}
successFlag = default;
// Fast zero-allocation copy from device for scalar: successFlag
fixed (global::System.Boolean* __gsharp_ptr_2 = &successFlag)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemcpyDtoHAsync_v2((global::System.IntPtr)__gsharp_ptr_2, __gsharp_d_2, (global::System.IntPtr)__gsharp_size_2, hStream));
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuStreamSynchronize(hStream));
}
}
finally
{
if (__gsharp_d_0 != global::System.IntPtr.Zero) global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemFreeAsync(__gsharp_d_0, hStream);
if (__gsharp_d_1 != global::System.IntPtr.Zero) global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemFreeAsync(__gsharp_d_1, hStream);
if (__gsharp_d_2 != global::System.IntPtr.Zero) global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemFreeAsync(__gsharp_d_2, hStream);
}
}
}
#pragma warning restore CS8500
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static unsafe void LaunchProcessPhysicsPtr(global::System.IntPtr particles, global::System.IntPtr totalMagnitude, global::System.IntPtr successFlag, global::DebugReduc.ComprehensiveSpecKernel.AotContexts.SpecTypes largeStructVal, global::System.Int32 particleCount, global::System.Int32 unrollFactor, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1,uint blockDimZ = 1, uint sharedMemBytes = 0, global::System.IntPtr hStream = default, global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
using (var __gsharp_cuda_context_scope = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
{
global::System.IntPtr __gsharp_hfunc = global::DebugReduc.ComprehensiveSpecKernel.__gsharp_hfunc_ProcessPhysics.Resolve();
global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare(__gsharp_hfunc, sharedMemBytes, "ComprehensiveSpecKernel.ProcessPhysics");
try
{
void** __gsharp_kernel_params = stackalloc void*[6];
// Marshal device array: particles
global::System.IntPtr* __gsharp_proxy_0 = stackalloc global::System.IntPtr[2];
__gsharp_proxy_0[0] = particles;
((int*)(__gsharp_proxy_0 + 1))[0] = 0; // Length is unknown for raw IntPtr
((int*)(__gsharp_proxy_0 + 1))[1] = 0;
__gsharp_kernel_params[0] = __gsharp_proxy_0;
// Marshal device scalar: totalMagnitude
__gsharp_kernel_params[1] = &totalMagnitude;
// Marshal device scalar: successFlag
__gsharp_kernel_params[2] = &successFlag;
// Marshal device struct: largeStructVal
__gsharp_kernel_params[3] = &largeStructVal;
// Marshal scalar: particleCount
__gsharp_kernel_params[4] = &particleCount;
// Marshal scalar: unrollFactor
__gsharp_kernel_params[5] = &unrollFactor;
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuLaunchKernel(
__gsharp_hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
hStream, // Stream
__gsharp_kernel_params,
global::System.IntPtr.Zero // Extra
));
}
finally
{
}
}
}
#pragma warning restore CS8500
public const string CooperativePhysicsProcess_Source = @"extern ""C"" __global__ void __gsharp_k_DebugReduc__ComprehensiveSpecKernel__CooperativePhysicsProcess(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_tid = (int)threadIdx.x;
__gsharp_id_0_data[__gsharp_id_1_tid]=__gsharp_id_1_tid;
cg::this_grid().sync();
}
";
public static readonly string CooperativePhysicsProcess_FullSource=global::Kassyi.GSharp.Generated.GSharpSharedCudaPrelude.Source +global::Kassyi.GSharp.Generated.GSharpSharedCudaDeviceCode.Source +CooperativePhysicsProcess_Source;
private static readonly global::Kassyi.GSharp.Runtime.Aot.CudaKernelFunction __gsharp_hfunc_CooperativePhysicsProcess = new global::Kassyi.GSharp.Runtime.Aot.CudaKernelFunction("__gsharp_k_DebugReduc__ComprehensiveSpecKernel__CooperativePhysicsProcess", global::Kassyi.GSharp.Generated.GSharpModuleContext_MegaTestCompilationBase.GetFatbin);
/// <summary>Preloads the CUDA module and resolves the function pointer for this kernel in the current CUDA context.</summary>
public static void WarmupFunctionPointerCooperativePhysicsProcess() => global::DebugReduc.ComprehensiveSpecKernel.__gsharp_hfunc_CooperativePhysicsProcess.Resolve();
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static unsafe void LaunchCooperativePhysicsProcess(global::System.Int32[] data, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1,uint blockDimZ = 1, uint sharedMemBytes = 0, global::System.IntPtr hStream = default, global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
if (data is null){
throw new global::System.ArgumentNullException(nameof(data));
}
if (data.Length == 0){
throw new global::System.ArgumentException("Kernel array arguments must not be empty. Declare the parameter as nullable (T[]? or Memory<T>?) to accept null or empty input.", nameof(data));
}
using (var __gsharp_cuda_context_scope = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
{
global::System.IntPtr __gsharp_hfunc = global::DebugReduc.ComprehensiveSpecKernel.__gsharp_hfunc_CooperativePhysicsProcess.Resolve();
global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare(__gsharp_hfunc, sharedMemBytes, "ComprehensiveSpecKernel.CooperativePhysicsProcess");
global::System.IntPtr __gsharp_d_0 = global::System.IntPtr.Zero;
try
{
void** __gsharp_kernel_params = stackalloc void*[1];
// Marshal array: data
int __gsharp_size_0 = checked(data.Length * sizeof(global::System.Int32));
global::System.IntPtr* __gsharp_proxy_0 = stackalloc global::System.IntPtr[2];
__gsharp_proxy_0[0] = global::System.IntPtr.Zero;
((int*)(__gsharp_proxy_0 + 1))[0] = data.Length;
((int*)(__gsharp_proxy_0 + 1))[1] = 0;
if (__gsharp_size_0 > 0)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemAllocAsync(out __gsharp_d_0, (global::System.IntPtr)__gsharp_size_0, hStream));
fixed (global::System.Int32* __gsharp_p_0 = data)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemcpyHtoDAsync_v2(__gsharp_d_0, (global::System.IntPtr)__gsharp_p_0, (global::System.IntPtr)__gsharp_size_0, hStream));
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuStreamSynchronize(hStream));
}
__gsharp_proxy_0[0] = __gsharp_d_0;
}
__gsharp_kernel_params[0] = __gsharp_proxy_0;
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuLaunchCooperativeKernel(
__gsharp_hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
hStream, // Stream
__gsharp_kernel_params
));
// Copy back array: data
if (__gsharp_size_0 > 0 && __gsharp_d_0 != global::System.IntPtr.Zero)
{
fixed (global::System.Int32* __gsharp_p_0 = data)
{
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemcpyDtoHAsync_v2((global::System.IntPtr)__gsharp_p_0, __gsharp_d_0, (global::System.IntPtr)__gsharp_size_0, hStream));
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuStreamSynchronize(hStream));
}
}
}
finally
{
if (__gsharp_d_0 != global::System.IntPtr.Zero) global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuMemFreeAsync(__gsharp_d_0, hStream);
}
}
}
#pragma warning restore CS8500
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static unsafe void LaunchCooperativePhysicsProcessPtr(global::System.IntPtr data, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1,uint blockDimZ = 1, uint sharedMemBytes = 0, global::System.IntPtr hStream = default, global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
using (var __gsharp_cuda_context_scope = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
{
global::System.IntPtr __gsharp_hfunc = global::DebugReduc.ComprehensiveSpecKernel.__gsharp_hfunc_CooperativePhysicsProcess.Resolve();
global::Kassyi.GSharp.Runtime.Aot.DynamicSharedMemoryOptIn.Prepare(__gsharp_hfunc, sharedMemBytes, "ComprehensiveSpecKernel.CooperativePhysicsProcess");
try
{
void** __gsharp_kernel_params = stackalloc void*[1];
// Marshal device array: data
global::System.IntPtr* __gsharp_proxy_0 = stackalloc global::System.IntPtr[2];
__gsharp_proxy_0[0] = data;
((int*)(__gsharp_proxy_0 + 1))[0] = 0; // Length is unknown for raw IntPtr
((int*)(__gsharp_proxy_0 + 1))[1] = 0;
__gsharp_kernel_params[0] = __gsharp_proxy_0;
global::Kassyi.GSharp.Runtime.Interop.CudaException.ThrowIfError(global::Kassyi.GSharp.Runtime.Interop.CudaDriverApi.cuLaunchCooperativeKernel(
__gsharp_hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
hStream, // Stream
__gsharp_kernel_params
));
}
finally
{
}
}
}
#pragma warning restore CS8500
}
}
```

<!-- END CPP -->

## 8.4 Sad Path Compilation Errors (Diagnostics)

To ensure that the constraints outlined in [01_language/05_limitations.md](../01_language/05_limitations.md) are strictly enforced, G# includes an exhaustive suite of diagnostic tests (`tests/Compiler/Kassyi.GSharp.Tests.Shared/Generator/GEN-05-07_GeneratorDiagnosticsTests.cs`).

For example, when attempting to write invalid C# code in a kernel:

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static int BadKernel()
{
    string s = "hello";
    object obj = new object();
    return 0;
}
```

The G# compiler natively intercepts these syntax tree nodes and fails the build with the following deterministic errors (which are snapshot-tested using Verify.XUnit):

1. **GSH0001**: `Object creation ('new') is not supported in GPU kernels. Only struct instantiation, SharedMemory<T> and an array of a constant size that initializes a local variable are allowed; use stackalloc for a temporary array.` (Triggered by `new object()`)
2. **GSH0002**: `The string type is not supported in GPU kernels` (Triggered by `string s = "hello"`)
3. **GSH0003**: `The syntax 'ObjectCreationExpression' is not supported in GSharp GPU kernels` (Fallback trigger for any unknown syntax node)
4. **GSH0004**: `Method 'BadKernel' is marked with [GpuKernel] but returns an unsupported type 'System.String'.`

## Conclusion

This explicit trace proves that:

1. `Constant` uses preprocessor macros.
2. Custom Structs map flawlessly, and all 11 primitive C# types align to their C++ equivalents.
3. Diagnostic warnings and errors natively integrate with MSBuild (`GSH0001` - `GSH0004`, `GSH0013` and `GSH0064`) to act as strict guardrails.
4. `CastExpression` and `ParenthesizedExpression` AST nodes are properly translated, allowing explicit math method casting like `(float)Math.Sqrt(...)`.
5. **Control Flow**: Complex `switch` statements, `for`, `while`, and `do-while` loops (including `continue` and `break` jumps) are correctly transpiled and verified by snapshot tests.
6. **Scalar Marshaling**: `ref` and `out` parameters are properly mapped to pointers in C++, and the C# host launcher utilizes zero-allocation `fixed` pinning to provide seamless bidirectional memory synchronization.

## See also

### Getting Started

- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)
- [3. The "Hello GPU" Tutorial](../00_getting_started/03_hello_gpu.md)

### Architecture

- [2. Compiler and Runtime Architecture](00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](01_aot_transpiler.md)
- [2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)](02_runtime_loader.md)
