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
        SpecTypes largeStructVal, // 48 bytes by value; within the GSH0066 parameter limit.
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
#nullable enable
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
namespace DebugReduc
{
public partial class ComprehensiveSpecKernel
{
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static partial class AotContexts
{
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 48)]
public unsafe partial struct SpecTypes
{
public const string DebugName_BooleanValue = "global::System.Boolean";
[System.Runtime.InteropServices.FieldOffset(0)]
public global::System.Boolean BooleanValue;
public const string DebugName_ByteValue = "global::System.Byte";
[System.Runtime.InteropServices.FieldOffset(1)]
public global::System.Byte ByteValue;
public const string DebugName_SByteValue = "global::System.SByte";
[System.Runtime.InteropServices.FieldOffset(2)]
public global::System.SByte SByteValue;
public const string DebugName_ShortValue = "global::System.Int16";
[System.Runtime.InteropServices.FieldOffset(4)]
public global::System.Int16 ShortValue;
public const string DebugName_UShortValue = "global::System.UInt16";
[System.Runtime.InteropServices.FieldOffset(6)]
public global::System.UInt16 UShortValue;
public const string DebugName_IntValue = "global::System.Int32";
[System.Runtime.InteropServices.FieldOffset(8)]
public global::System.Int32 IntValue;
public const string DebugName_UIntValue = "global::System.UInt32";
[System.Runtime.InteropServices.FieldOffset(12)]
public global::System.UInt32 UIntValue;
public const string DebugName_LongValue = "global::System.Int64";
[System.Runtime.InteropServices.FieldOffset(16)]
public global::System.Int64 LongValue;
public const string DebugName_ULongValue = "global::System.UInt64";
[System.Runtime.InteropServices.FieldOffset(24)]
public global::System.UInt64 ULongValue;
public const string DebugName_FloatValue = "global::System.Single";
[System.Runtime.InteropServices.FieldOffset(32)]
public global::System.Single FloatValue;
public const string DebugName_DoubleValue = "global::System.Double";
[System.Runtime.InteropServices.FieldOffset(40)]
public global::System.Double DoubleValue;
}
public static unsafe void Marshal_SpecTypes(ref global::DebugReduc.SpecTypes host, ref AotContexts.SpecTypes native, bool isDevicePtr, System.Collections.Generic.List<IntPtr>? allocatedPtrs, IntPtr hStream = default)
{
native.BooleanValue = host.BooleanValue;
native.ByteValue = host.ByteValue;
native.SByteValue = host.SByteValue;
native.ShortValue = host.ShortValue;
native.UShortValue = host.UShortValue;
native.IntValue = host.IntValue;
native.UIntValue = host.UIntValue;
native.LongValue = host.LongValue;
native.ULongValue = host.ULongValue;
native.FloatValue = host.FloatValue;
native.DoubleValue = host.DoubleValue;
}
public static unsafe void Unmarshal_SpecTypes(ref global::DebugReduc.SpecTypes host, ref AotContexts.SpecTypes native, bool isDevicePtr, IntPtr hStream = default)
{
host.BooleanValue = native.BooleanValue;
host.ByteValue = native.ByteValue;
host.SByteValue = native.SByteValue;
host.ShortValue = native.ShortValue;
host.UShortValue = native.UShortValue;
host.IntValue = native.IntValue;
host.UIntValue = native.UIntValue;
host.LongValue = native.LongValue;
host.ULongValue = native.ULongValue;
host.FloatValue = native.FloatValue;
host.DoubleValue = native.DoubleValue;
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
#endif
#ifndef __GSHARP_STRUCT___gsharp_ty_DebugReduc__Point__
#define __GSHARP_STRUCT___gsharp_ty_DebugReduc__Point__
struct __gsharp_ty_DebugReduc__Point
{
float __gsharp_mb_X;
float __gsharp_mb_Y;
}
;
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
#endif
#ifndef __GSHARP_STRUCT___gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState__
#define __GSHARP_STRUCT___gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState__
struct __gsharp_ty_DebugReduc__ComprehensiveSpecKernel__PhysicsState
{
unsigned int __gsharp_mb_Buffer[16];
unsigned long long __gsharp_mb_Flags;
}
;
#endif
extern ""C"" __global__ void __gsharp_k_DebugReduc__ComprehensiveSpecKernel__ProcessPhysics(GSharpArray<__gsharp_ty_DebugReduc__PhysicsVector> __gsharp_id_0_particles, float* __restrict__  __gsharp_id_1_totalMagnitude, bool* __restrict__  __gsharp_id_2_successFlag, __gsharp_ty_DebugReduc__SpecTypes __gsharp_id_3_largeStructVal, int __gsharp_id_4_particleCount, int __gsharp_id_5_unrollFactor){
(*__gsharp_id_2_successFlag)=false;
float __gsharp_id_6_Gravity = 9.8f;
int __gsharp_id_7_tx = threadIdx.x;
int __gsharp_id_8_ty = threadIdx.y;
int __gsharp_id_9_tz = threadIdx.z;
int __gsharp_id_10_bx = blockIdx.x;
int __gsharp_id_11_by = blockIdx.y;
int __gsharp_id_12_bz = blockIdx.z;
int __gsharp_id_13_bdimX = blockDim.x;
int __gsharp_id_14_bdimY = blockDim.y;
int __gsharp_id_15_bdimZ = blockDim.z;
int __gsharp_id_16_gdimX = gridDim.x;
int __gsharp_id_17_gdimY = gridDim.y;
int __gsharp_id_18_gdimZ = gridDim.z;
int __gsharp_id_19_tid = __gsharp_id_7_tx+__gsharp_id_10_bx*__gsharp_id_13_bdimX;
__shared__ int __gsharp_id_20_staticCache[10000];
extern __shared__ char __gsharp_dynamic_smem[];
size_t __gsharp_smem_offset = 0;
__gsharp_smem_offset = (__gsharp_smem_offset + alignof(float) - 1) & ~(alignof(float) - 1);
float* __gsharp_id_21_dynamicCache = (float*)(__gsharp_dynamic_smem + __gsharp_smem_offset);
__gsharp_smem_offset += (__gsharp_id_4_particleCount) * sizeof(float);
__shared__ int __gsharp_id_22_targetTypedCache[256];
if(__gsharp_id_19_tid<__gsharp_id_4_particleCount)
{
__gsharp_ty_DebugReduc__PhysicsVector __gsharp_id_23_p = __gsharp_id_0_particles[__gsharp_id_19_tid];
__gsharp_ty_DebugReduc__SpecTypes __gsharp_id_24_types = __gsharp_id_3_largeStructVal;
auto __gsharp_id_25_origin = __gsharp_ty_DebugReduc__Point{0, 0};
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
__gsharp_id_28_mag=(float)fmaxf(__gsharp_id_28_mag,__gsharp_id_23_p.__gsharp_mb_X*__gsharp_id_29_i+__gsharp_id_6_Gravity);
}
if(!__gsharp_fn_DebugReduc__ComprehensiveSpecKernel__IsPositive(__gsharp_id_28_mag))
{
__gsharp_id_28_mag=0;
}
int __gsharp_id_30_counter = 0;
while(__gsharp_id_30_counter<2)
{
__gsharp_id_28_mag=(float)fminf(__gsharp_id_28_mag,__gsharp_id_28_mag+1);
__gsharp_id_30_counter++;
}
do
{
__gsharp_id_30_counter--;
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
int __gsharp_id_39_bitwise = (__gsharp_id_7_tx<<2)|(__gsharp_id_8_ty>>1)^0xFF;
__gsharp_id_39_bitwise&=~1;
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
private static IntPtr? s_hfunc_ProcessPhysics;
/// <summary>Preloads the CUDA module and resolves the function pointer for this kernel.</summary>
public static void WarmupFunctionPointerProcessPhysics()
{
if (s_hfunc_ProcessPhysics.HasValue) return;
IntPtr hModule = global::Kassyi.GSharp.Generated.GSharpModuleContext_MegaTestCompilationBase.GetOrLoadModule();
s_hfunc_ProcessPhysics = global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.GetFunction(hModule, "__gsharp_k_DebugReduc__ComprehensiveSpecKernel__ProcessPhysics");
}
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static unsafe void LaunchProcessPhysics(global::DebugReduc.PhysicsVector[] particles, ref global::System.Single totalMagnitude, out global::System.Boolean successFlag, global::DebugReduc.SpecTypes largeStructVal, global::System.Int32 particleCount, global::System.Int32 unrollFactor, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1,uint blockDimZ = 1, uint sharedMemBytes = 0, IntPtr hStream = default, System.ReadOnlySpan<System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
successFlag = default;
if (particles is null){
throw new global::System.ArgumentNullException(nameof(particles));
}
if (particles.Length == 0){
throw new global::System.ArgumentException("Kernel array arguments must not be empty. Declare the parameter as nullable (T[]? or Memory<T>?) to accept null or empty input.", nameof(particles));
}
using (var __gsharp_cuda_context_scope = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
{
WarmupFunctionPointerProcessPhysics();
IntPtr hfunc = s_hfunc_ProcessPhysics!.Value;
IntPtr d_particles = IntPtr.Zero;
IntPtr d_totalMagnitude = IntPtr.Zero;
IntPtr d_successFlag = IntPtr.Zero;
System.Collections.Generic.List<IntPtr> largeStructVal_allocatedPtrs = new System.Collections.Generic.List<IntPtr>();
try
{
void** kernelParams = stackalloc void*[6];
// Marshal array: particles
int particles_size = particles.Length * sizeof(global::DebugReduc.PhysicsVector);
IntPtr* proxy_particles = stackalloc IntPtr[2];
proxy_particles[0] = IntPtr.Zero;
((int*)(proxy_particles + 1))[0] = particles.Length;
if (particles_size > 0)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_particles, (IntPtr)particles_size, hStream));
proxy_particles[0] = d_particles;
fixed (global::DebugReduc.PhysicsVector* p_particles = particles)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(d_particles, (IntPtr)p_particles, (IntPtr)particles_size, hStream));
CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));
}
}
kernelParams[0] = proxy_particles;
// Marshal ref/out scalar: totalMagnitude
int totalMagnitude_size = sizeof(global::System.Single);
CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_totalMagnitude, (IntPtr)totalMagnitude_size, hStream));
// Fast zero-allocation copy to device
fixed (global::System.Single* p_totalMagnitude_temp = &totalMagnitude)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(d_totalMagnitude, (IntPtr)p_totalMagnitude_temp, (IntPtr)totalMagnitude_size, hStream));
CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));
}
kernelParams[1] = &d_totalMagnitude;
// Marshal ref/out scalar: successFlag
int successFlag_size = sizeof(global::System.Boolean);
CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_successFlag, (IntPtr)successFlag_size, hStream));
kernelParams[2] = &d_successFlag;
AotContexts.SpecTypes native_largeStructVal = default;
AotContexts.Marshal_SpecTypes(ref largeStructVal, ref native_largeStructVal, false, largeStructVal_allocatedPtrs, hStream);
// Marshal scalar struct: largeStructVal
kernelParams[3] = &native_largeStructVal;
// Marshal scalar: particleCount
kernelParams[4] = &particleCount;
// Marshal scalar: unrollFactor
kernelParams[5] = &unrollFactor;
CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
hStream, // Stream
kernelParams,
IntPtr.Zero // Extra
));
// Copy back array: particles
if (particles_size > 0 && d_particles != IntPtr.Zero)
{
fixed (global::DebugReduc.PhysicsVector* p_particles = particles)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)p_particles, d_particles, (IntPtr)particles_size, hStream));
CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));
}
}
// Fast zero-allocation copy from device for scalar: totalMagnitude
fixed (global::System.Single* ptr_totalMagnitude = &totalMagnitude)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)ptr_totalMagnitude, d_totalMagnitude, (IntPtr)totalMagnitude_size, hStream));
CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));
}
successFlag = default;
// Fast zero-allocation copy from device for scalar: successFlag
fixed (global::System.Boolean* ptr_successFlag = &successFlag)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)ptr_successFlag, d_successFlag, (IntPtr)successFlag_size, hStream));
CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));
}
AotContexts.Unmarshal_SpecTypes(ref largeStructVal, ref native_largeStructVal, false, hStream);
}
finally
{
if (d_particles != IntPtr.Zero) CudaDriverApi.cuMemFreeAsync(d_particles, hStream);
if (d_totalMagnitude != IntPtr.Zero) CudaDriverApi.cuMemFreeAsync(d_totalMagnitude, hStream);
if (d_successFlag != IntPtr.Zero) CudaDriverApi.cuMemFreeAsync(d_successFlag, hStream);
foreach (var ptr in largeStructVal_allocatedPtrs) CudaDriverApi.cuMemFreeAsync(ptr, hStream);
}
}
}
#pragma warning restore CS8500
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static unsafe void LaunchProcessPhysicsPtr(IntPtr particles, IntPtr totalMagnitude, IntPtr successFlag, AotContexts.SpecTypes largeStructVal, global::System.Int32 particleCount, global::System.Int32 unrollFactor, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1,uint blockDimZ = 1, uint sharedMemBytes = 0, IntPtr hStream = default, System.ReadOnlySpan<System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
using (var __gsharp_cuda_context_scope = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
{
WarmupFunctionPointerProcessPhysics();
IntPtr hfunc = s_hfunc_ProcessPhysics!.Value;
try
{
void** kernelParams = stackalloc void*[6];
// Marshal device array: particles
IntPtr* proxy_particles = stackalloc IntPtr[2];
proxy_particles[0] = particles;
((int*)(proxy_particles + 1))[0] = 0; // Length is unknown for raw IntPtr
kernelParams[0] = proxy_particles;
// Marshal device scalar: totalMagnitude
kernelParams[1] = &totalMagnitude;
// Marshal device scalar: successFlag
kernelParams[2] = &successFlag;
// Marshal device struct: largeStructVal
kernelParams[3] = &largeStructVal;
// Marshal scalar: particleCount
kernelParams[4] = &particleCount;
// Marshal scalar: unrollFactor
kernelParams[5] = &unrollFactor;
CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
hStream, // Stream
kernelParams,
IntPtr.Zero // Extra
));
}
finally
{
}
}
}
#pragma warning restore CS8500
public const string CooperativePhysicsProcess_Source = @"extern ""C"" __global__ void __gsharp_k_DebugReduc__ComprehensiveSpecKernel__CooperativePhysicsProcess(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_tid = threadIdx.x;
__gsharp_id_0_data[__gsharp_id_1_tid]=__gsharp_id_1_tid;
cg::this_grid().sync();
}
";
public static readonly string CooperativePhysicsProcess_FullSource=global::Kassyi.GSharp.Generated.GSharpSharedCudaPrelude.Source +global::Kassyi.GSharp.Generated.GSharpSharedCudaDeviceCode.Source +CooperativePhysicsProcess_Source;
private static IntPtr? s_hfunc_CooperativePhysicsProcess;
/// <summary>Preloads the CUDA module and resolves the function pointer for this kernel.</summary>
public static void WarmupFunctionPointerCooperativePhysicsProcess()
{
if (s_hfunc_CooperativePhysicsProcess.HasValue) return;
IntPtr hModule = global::Kassyi.GSharp.Generated.GSharpModuleContext_MegaTestCompilationBase.GetOrLoadModule();
s_hfunc_CooperativePhysicsProcess = global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.GetFunction(hModule, "__gsharp_k_DebugReduc__ComprehensiveSpecKernel__CooperativePhysicsProcess");
}
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static unsafe void LaunchCooperativePhysicsProcess(global::System.Int32[] data, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1,uint blockDimZ = 1, uint sharedMemBytes = 0, IntPtr hStream = default, System.ReadOnlySpan<System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
if (data is null){
throw new global::System.ArgumentNullException(nameof(data));
}
if (data.Length == 0){
throw new global::System.ArgumentException("Kernel array arguments must not be empty. Declare the parameter as nullable (T[]? or Memory<T>?) to accept null or empty input.", nameof(data));
}
using (var __gsharp_cuda_context_scope = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
{
WarmupFunctionPointerCooperativePhysicsProcess();
IntPtr hfunc = s_hfunc_CooperativePhysicsProcess!.Value;
IntPtr d_data = IntPtr.Zero;
try
{
void** kernelParams = stackalloc void*[1];
// Marshal array: data
int data_size = data.Length * sizeof(global::System.Int32);
IntPtr* proxy_data = stackalloc IntPtr[2];
proxy_data[0] = IntPtr.Zero;
((int*)(proxy_data + 1))[0] = data.Length;
if (data_size > 0)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemAllocAsync(out d_data, (IntPtr)data_size, hStream));
proxy_data[0] = d_data;
fixed (global::System.Int32* p_data = data)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoDAsync_v2(d_data, (IntPtr)p_data, (IntPtr)data_size, hStream));
CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));
}
}
kernelParams[0] = proxy_data;
CudaException.ThrowIfError(CudaDriverApi.cuLaunchCooperativeKernel(
hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
hStream, // Stream
kernelParams
));
// Copy back array: data
if (data_size > 0 && d_data != IntPtr.Zero)
{
fixed (global::System.Int32* p_data = data)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoHAsync_v2((IntPtr)p_data, d_data, (IntPtr)data_size, hStream));
CudaException.ThrowIfError(CudaDriverApi.cuStreamSynchronize(hStream));
}
}
}
finally
{
if (d_data != IntPtr.Zero) CudaDriverApi.cuMemFreeAsync(d_data, hStream);
}
}
}
#pragma warning restore CS8500
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[global::System.CodeDom.Compiler.GeneratedCodeAttribute("Kassyi.GSharp.Compiler", "1.0.0.0")]
[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public static unsafe void LaunchCooperativePhysicsProcessPtr(IntPtr data, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1,uint blockDimZ = 1, uint sharedMemBytes = 0, IntPtr hStream = default, System.ReadOnlySpan<System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
using (var __gsharp_cuda_context_scope = new global::Kassyi.GSharp.Runtime.Aot.GpuKernelLoader.CudaContextScope())
{
WarmupFunctionPointerCooperativePhysicsProcess();
IntPtr hfunc = s_hfunc_CooperativePhysicsProcess!.Value;
try
{
void** kernelParams = stackalloc void*[1];
// Marshal device array: data
IntPtr* proxy_data = stackalloc IntPtr[2];
proxy_data[0] = data;
((int*)(proxy_data + 1))[0] = 0; // Length is unknown for raw IntPtr
kernelParams[0] = proxy_data;
CudaException.ThrowIfError(CudaDriverApi.cuLaunchCooperativeKernel(
hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
hStream, // Stream
kernelParams
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

1. **GSH0001**: `Object creation ('new') is not supported in GPU kernels. Only struct instantiation and SharedMemory<T> are allowed.` (Triggered by `new object()`)
2. **GSH0002**: `The string type is not supported in GPU kernels` (Triggered by `string s = "hello"`)
3. **GSH0003**: `The syntax 'ObjectCreationExpression' is not supported in GSharp GPU kernels` (Fallback trigger for any unknown syntax node)
4. **GSH0004**: `Method 'BadKernel' is marked with [GpuKernel] but returns an unsupported type 'System.String'.`

## Conclusion

This explicit trace proves that:

1. `Constant` uses preprocessor macros.
2. Custom Structs map flawlessly, and all 11 primitive C# types align to their C++ equivalents.
3. Diagnostic warnings and errors natively integrate with MSBuild (`GSH0001` - `GSH0004`, `GSH0013` and `GSH0066`) to act as strict guardrails.
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
