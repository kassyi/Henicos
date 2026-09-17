# 8. Comprehensive Example

This document provides a "Rosetta Stone" example that validates all of the specifications detailed in this repository. It demonstrates how a single G# C# file utilizes custom `struct` definitions, `[GpuKernel]`, `[GpuDeviceFunction]`, `[Constant]`, intrinsic math functions, static/dynamic shared memory, atomics, thread synchronization, and implicit array marshaling.

## 8.1 The G# Source Code

// Context: G# Kernel Definitions

<!-- BEGIN CSHARP -->
```csharp
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
```
<!-- END CSHARP -->

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
```
<!-- END CSHARP -->

## 8.2 Build Output (Diagnostics)

When building this code, MSBuild natively catches the strict constraints via the `Kassyi.Generators.GSharp` analyzers:

```
ComprehensiveSpecKernel.cs(50,9): warning GSH1002: The parameter 'largeStructVal' is a struct of size 43 bytes. Passing large structs by value to a GPU kernel can consume excessive constant memory. Consider passing by reference (ref) or using an array pointer.
ComprehensiveSpecKernel.cs(76,63): warning GSH1001: The kernel allocates 40000 bytes of static shared memory, which exceeds the recommended limit of 32768 bytes. This may cause kernel launch failure or reduce occupancy.
```

## 8.3 The Transpiled CUDA C++ Output

// Context: Transpiled CUDA C++ Code

<!-- BEGIN CPP -->
```cpp
﻿//HintName: ComprehensiveSpecKernel_ProcessPhysics_Wrapper.g.cs
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
namespace DebugReduc
{
public static partial class ComprehensiveSpecKernelWrapper
{
public const string ProcessPhysics_Source = @"#ifndef __GSHARP_STRUCT_DebugReduc_PhysicsVector__
#define __GSHARP_STRUCT_DebugReduc_PhysicsVector__
struct DebugReduc_PhysicsVector
{
float X;
float Y;
float Z;
}
;
#endif
#ifndef __GSHARP_STRUCT_DebugReduc_SpecTypes__
#define __GSHARP_STRUCT_DebugReduc_SpecTypes__
struct DebugReduc_SpecTypes
{
bool BooleanValue;
unsigned char ByteValue;
signed char SByteValue;
short ShortValue;
unsigned short UShortValue;
int IntValue;
unsigned int UIntValue;
long long LongValue;
unsigned long long ULongValue;
float FloatValue;
double DoubleValue;
}
;
#endif
#ifndef __GSHARP_STRUCT_DebugReduc_Point__
#define __GSHARP_STRUCT_DebugReduc_Point__
struct DebugReduc_Point
{
float X;
float Y;
}
;
#endif
#ifndef __GSHARP_STRUCT_DebugReduc_NestedPhysicsState__
#define __GSHARP_STRUCT_DebugReduc_NestedPhysicsState__
struct DebugReduc_NestedPhysicsState
{
DebugReduc_PhysicsVector Position;
DebugReduc_PhysicsVector Velocity;
unsigned long long StatusFlag;
}
;
#endif
#ifndef __GSHARP_STRUCT_DebugReduc_ComprehensiveSpecKernel_Vector8__
#define __GSHARP_STRUCT_DebugReduc_ComprehensiveSpecKernel_Vector8__
struct DebugReduc_ComprehensiveSpecKernel_Vector8
{
unsigned int _element0[8];
__device__ inline unsigned int& operator[](int index){
return _element0[index];
}
__device__ inline const unsigned int& operator[](int index) const
{
return _element0[index];
}
__device__ inline unsigned int* begin() { return _element0; }
__device__ inline unsigned int* end() { return _element0 + 8; }
__device__ inline const unsigned int* begin() const { return _element0; }
__device__ inline const unsigned int* end() const { return _element0 + 8; }
__device__ inline int size() const { return 8; }
}
;
#endif
#ifndef __GSHARP_STRUCT_DebugReduc_ComprehensiveSpecKernel_PhysicsState__
#define __GSHARP_STRUCT_DebugReduc_ComprehensiveSpecKernel_PhysicsState__
struct DebugReduc_ComprehensiveSpecKernel_PhysicsState
{
unsigned int Buffer[16];
unsigned long long Flags;
}
;
#endif
extern ""C"" __declspec(dllexport) void ProcessPhysics(DebugReduc_PhysicsVector* __restrict__ particles, float* __restrict__  totalMagnitude, bool* __restrict__  successFlag, DebugReduc_SpecTypes largeStructVal, int particleCount, int unrollFactor){
(*successFlag)=false;
float Gravity = 9.8f;
int tx = threadIdx.x;
int ty = threadIdx.y;
int tz = threadIdx.z;
int bx = blockIdx.x;
int by = blockIdx.y;
int bz = blockIdx.z;
int bdimX = blockDim.x;
int bdimY = blockDim.y;
int bdimZ = blockDim.z;
int gdimX = gridDim.x;
int gdimY = gridDim.y;
int gdimZ = gridDim.z;
int tid = tx+bx*bdimX;
__shared__ int staticCache[10000];
extern __shared__ char __gsharp_dynamic_smem[];
size_t __smem_offset = 0;
__smem_offset = (__smem_offset + alignof(float) - 1) & ~(alignof(float) - 1);
float* dynamicCache = (float*)(__gsharp_dynamic_smem + __smem_offset);
__smem_offset += (particleCount) * sizeof(float);
__shared__ int targetTypedCache[256];
if(tid<particleCount)
{
DebugReduc_PhysicsVector p = particles[tid];
DebugReduc_SpecTypes types = largeStructVal;
auto origin = DebugReduc_Point{0, 0};
auto distance = origin.X+p.X;
auto nested = ([&]() {
DebugReduc_NestedPhysicsState _tmp{}; _tmp.Position = p; _tmp.Velocity = p; _tmp.StatusFlag = 1; return _tmp; }
());
float mag = ComputeMagnitude(p);
mag=(float)fabsf(mag);
for(int i = 0;i<unrollFactor;i++)
{
if(i==1)
{
continue;
}
mag=(float)fmaxf(mag,p.X*i+Gravity);
}
if(!IsPositive(mag))
{
mag=0;
}
int counter = 0;
while(counter<2)
{
mag=(float)fminf(mag,mag+1);
counter++;
}
do
{
counter--;
}
while(counter>0);
switch(tx)
{
case 0:
mag+=1;
break;
default:
mag+=0;
break;
}
staticCache[tx]=tid;
dynamicCache[tx]=mag;
__syncthreads();
if(tx==0)
{
atomicAdd((*totalMagnitude),dynamicCache[0]);
(*successFlag)=true;
}
DebugReduc_ComprehensiveSpecKernel_Vector8 inlineVec = DebugReduc_ComprehensiveSpecKernel_Vector8{};
inlineVec[0]=123;
inlineVec[1]=456;
{
DebugReduc_ComprehensiveSpecKernel_PhysicsState state = DebugReduc_ComprehensiveSpecKernel_PhysicsState{};
DebugReduc_ComprehensiveSpecKernel_PhysicsState* pState = &state;
pState.Buffer[0]=inlineVec[0];
(*pState).Buffer[1]=inlineVec[1];
unsigned long long carry = 0;
unsigned int sum = __gsharp_add_cc(pState.Buffer[0],pState.Buffer[1],&carry);
long long borrow = 0;
unsigned int diff = __gsharp_sub_cc(sum,pState.Buffer[1],&borrow);
if(sum>diff)
{
pState.Flags=1;
}
}
bool condition1 = tx>0;
bool condition2 = ty==0;
if(condition1&&condition2)
{
int bitwise = (tx<<2)|(ty>>1)^0xFF;
bitwise&=~1;
}
else
{
if(condition1||!condition2)
{
mag+=1.5f;
}
}
InlinePtx(""/* test ptx inline */"");
if(ty>0)
{
return;
}
}
}
extern ""C"" __declspec(dllexport) void __gsharp_launch_ProcessPhysics(uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ, DebugReduc_PhysicsVector* __restrict__ particles, float* __restrict__  totalMagnitude, bool* __restrict__  successFlag, DebugReduc_SpecTypes largeStructVal, int particleCount, int unrollFactor)
{
for (uint32_t bz = 0; bz < gridDimZ; bz++)
for (uint32_t by = 0; by < gridDimY; by++)
for (uint32_t bx = 0; bx < gridDimX; bx++)
for (uint32_t tz = 0; tz < blockDimZ; tz++)
for (uint32_t ty = 0; ty < blockDimY; ty++)
for (uint32_t tx = 0; tx < blockDimX; tx++)
{
__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);
ProcessPhysics(particles, totalMagnitude, successFlag, largeStructVal, particleCount, unrollFactor);
}
}
";
public static readonly string ProcessPhysics_FullSource = global::Kassyi.GSharp.Generated.GSharpSharedCpuPrelude.Source + global::Kassyi.GSharp.Generated.GSharpSharedCpuDeviceCode.Source + ProcessPhysics_Source;
private static byte[]? s_decryptionKey_ProcessPhysics;
private static byte[]? s_binaryData_ProcessPhysics;
private static bool s_isLoaded_ProcessPhysics;
#pragma warning disable CS8500 // Takes the address of, gets the size of, or declares a pointer to a managed type
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct __Native_DebugReduc_SpecTypes
{
public global::System.Boolean BooleanValue;
public global::System.Byte ByteValue;
public global::System.SByte SByteValue;
public global::System.Int16 ShortValue;
public global::System.UInt16 UShortValue;
public global::System.Int32 IntValue;
public global::System.UInt32 UIntValue;
public global::System.Int64 LongValue;
public global::System.UInt64 ULongValue;
public global::System.Single FloatValue;
public global::System.Double DoubleValue;
}
public static unsafe __Native_DebugReduc_SpecTypes Marshal_DebugReduc_SpecTypes(ref global::DebugReduc.SpecTypes host, bool isDevicePtr, System.Collections.Generic.List<IntPtr>? allocatedPtrs)
{
var native = new __Native_DebugReduc_SpecTypes();
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
return native;
}
public static unsafe void Unmarshal_DebugReduc_SpecTypes(ref global::DebugReduc.SpecTypes host, ref __Native_DebugReduc_SpecTypes native, bool isDevicePtr)
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
#pragma warning disable CS8500
[System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
private unsafe delegate void ProcessPhysics_Delegate(uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY, uint blockDimZ, global::DebugReduc.PhysicsVector* particles, global::System.Single* totalMagnitude, global::System.Boolean* successFlag, global::DebugReduc.SpecTypes largeStructVal, global::System.Int32 particleCount);
public static unsafe void Launch_ProcessPhysics(global::DebugReduc.PhysicsVector[] particles, ref global::System.Single totalMagnitude, out global::System.Boolean successFlag, global::DebugReduc.SpecTypes largeStructVal, global::System.Int32 particleCount, global::System.Int32 unrollFactor, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1, uint blockDimZ = 1, uint sharedMemBytes = 0, System.ReadOnlySpan<System.Collections.Generic.KeyValuePair<string, string>> macros = default)
{
successFlag = default;
if (particles.Length == 0){
return;
}
if (!s_isLoaded_ProcessPhysics)
{
s_decryptionKey_ProcessPhysics = null;
s_isLoaded_ProcessPhysics = true;
}
var archSuffix = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "_arm64" : "_x64";
var dllName = $"ComprehensiveSpecKernel_ProcessPhysics{archSuffix}.dll";
var dllPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, dllName);
var hfunc = global::Kassyi.GSharp.Runtime.Aot.CpuKernelLoader.LoadAotKernel<ProcessPhysics_Delegate>("__gsharp_launch_ProcessPhysics", dllPath, s_decryptionKey_ProcessPhysics);
fixed (global::DebugReduc.PhysicsVector* p_particles = particles)
fixed (global::System.Single* p_totalMagnitude = &totalMagnitude)
fixed (global::System.Boolean* p_successFlag = &successFlag)
{
hfunc(gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ, p_particles, p_totalMagnitude, p_successFlag, largeStructVal, particleCount);
}
}
#pragma warning restore CS8500
}
}
```
<!-- END CPP -->

## 8.4 Sad Path Compilation Errors (Diagnostics)

To ensure that the constraints outlined in [../../01_language/05_limitations.md](../../01_language/05_limitations.md) are strictly enforced, G# includes an exhaustive suite of diagnostic tests (`tests/Kassyi.GSharp.Tests.Shared/Generator/GEN-05-07_GeneratorDiagnosticsTests.cs`).

For example, when attempting to write invalid C# code in a kernel:

```csharp
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
4. **GSH0004**: `Method 'BadKernel' is marked with [GpuKernel] but returns 'System.Int32'. GpuKernel methods must return void.`

## Conclusion

This explicit trace proves that:

1. `Constant` uses preprocessor macros.
2. Custom Structs map flawlessly, and all 11 primitive C# types align to their C++ equivalents.
3. Diagnostic warnings and errors natively integrate with MSBuild (`GSH0001` - `GSH0004` and `GSH1001` - `GSH1002`) to act as strict guardrails.
4. `CastExpression` and `ParenthesizedExpression` AST nodes are properly translated, allowing explicit math method casting like `(float)Math.Sqrt(...)`.
5. **Control Flow**: Complex `switch` statements, `for`, `while`, and `do-while` loops (including `continue` and `break` jumps) are correctly transpiled and verified by snapshot tests.
6. **Scalar Marshaling**: `ref` and `out` parameters are properly mapped to pointers in C++, and the C# host launcher utilizes zero-allocation `fixed` pinning to provide seamless bidirectional memory synchronization.
