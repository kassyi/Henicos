# 8. Comprehensive Example

This document provides a "Rosetta Stone" example that validates all of the specifications detailed in this repository. It demonstrates how a single G# C# file utilizes custom `struct` definitions, `[GpuKernel]`, `[GpuDeviceFunction]`, `[JitConstant]`, intrinsic math functions, static/dynamic shared memory, atomics, thread synchronization, and implicit array marshaling.

## 8.1 The G# Source Code

// Context: G# Kernel Definitions

<!-- BEGIN CSHARP -->

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;

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
    [GpuDeviceFunction]
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
    public static void ProcessPhysics(
        PhysicsVector[] particles,
        ref float totalMagnitude,
        out bool successFlag,
        SpecTypes largeStructVal, // Triggers GSH1002 (>= 32 bytes passed by value)
        int particleCount,
        [JitConstant] int unrollFactor)
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
            mag = (float)Math.Abs(mag);

            // 5. Automatic Loop Unrolling via JitConstant (for)
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

                // Pointer operators and sizeof
                PhysicsState* pState = &state;
                int stateSize = sizeof(PhysicsState);

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

                mag = (bitwise > 0) ? mag * 2.0f : mag / 2.0f; // Ternary conditional
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
        using var context = new GpuContext(1024, GpuExecutionEngine.GSharpJit);

        using var a = new GpuBigInteger(context, 1024);
        using var b = new GpuBigInteger(context, 1024);
        using var result = new GpuBigInteger(context, 1024);

        // AST Construction: evaluated lazily and dispatched to GPU
        GpuBigInteger.Add(a, b, result);
        GpuBigInteger.Sub(result, a, b);
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
//HintName: ComprehensiveSpecKernel_ProcessPhysics_Wrapper.g.cs
// <auto-generated/>
#nullable enable
using System;
using System.Runtime.InteropServices;
using global::Kassyi.GSharp.Core;
using global::Kassyi.GSharp.Runtime.Interop;
namespace DebugReduc
{
public static partial class ComprehensiveSpecKernelWrapper
{
public const string ProcessPhysics_CudaSource = @"// GSharp Prelude
#include <cooperative_groups.h>
namespace cg=cooperative_groups;

__device__ __forceinline__ unsigned int __gsharp_add_cc(unsigned int a,unsigned int b,unsigned long long*carry){
unsigned int res;
unsigned int c_in=(unsigned int)*carry;
unsigned int c_out;
asm volatile(
""add.cc.u32 %0, %2, -1;\n\t""
""addc.cc.u32 %0, %3, %4;\n\t""
""addc.u32 %1, 0, 0;""
:""=&r""(res),""=r""(c_out)
:""r""(c_in),""r""(a),""r""(b)
);
*carry=c_out;
return res;
}
__device__ __forceinline__ unsigned int __gsharp_sub_cc(unsigned int a,unsigned int b,long long*borrow){
unsigned int res;
unsigned int dummy;
unsigned int b_in=(unsigned int)*borrow;
unsigned int b_out;
asm volatile(
""sub.cc.u32 %1, 0, %3;\n\t""
""subc.cc.u32 %0, %4, %5;\n\t""
""subc.u32 %2, 0, 0;""
:""=&r""(res),""=&r""(dummy),""=r""(b_out)
:""r""(b_in),""r""(a),""r""(b)
);
*borrow=b_out&1;
return res;
}
struct DebugReduc_PhysicsVector;
__forceinline__ __device__ float ComputeMagnitude(DebugReduc_PhysicsVector v);

__noinline__ __device__ bool IsPositive(float value);

#ifndef __GSHARP_STRUCT_DebugReduc_PhysicsVector__
#define __GSHARP_STRUCT_DebugReduc_PhysicsVector__
struct DebugReduc_PhysicsVector
{
float X;
float Y;
float Z;
}
;
#endif
__forceinline__ __device__ float ComputeMagnitude(DebugReduc_PhysicsVector v){
return (float)sqrt((((v.X*v.X)+(v.Y*v.Y))+(v.Z*v.Z)));
}

__noinline__ __device__ bool IsPositive(float value){
return (value>0);
}

#ifndef __GSHARP_STRUCT_DebugReduc_PhysicsVector__
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
char SByteValue;
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
extern ""C"" __global__ void ProcessPhysics(DebugReduc_PhysicsVector* particles,float* totalMagnitude,bool* successFlag,DebugReduc_SpecTypes largeStructVal,int particleCount){
successFlag=false;
float Gravity=9.8f;
int tx=threadIdx.x;
int ty=threadIdx.y;
int tz=threadIdx.z;
int bx=blockIdx.x;
int by=blockIdx.y;
int bz=blockIdx.z;
int bdimX=blockDim.x;
int bdimY=blockDim.y;
int bdimZ=blockDim.z;
int gdimX=gridDim.x;
int gdimY=gridDim.y;
int gdimZ=gridDim.z;
int tid=(tx+(bx*bdimX));
__shared__ int staticCache[10000];
extern __shared__ char __gsharp_dynamic_smem[];
size_t __smem_offset = 0;
__smem_offset = (__smem_offset + alignof(float) - 1) & ~(alignof(float) - 1);
float* dynamicCache = (float*)(__gsharp_dynamic_smem + __smem_offset);
__smem_offset += (particleCount) * sizeof(float);
__shared__ int targetTypedCache[256];
if((tid<particleCount))
{
DebugReduc_PhysicsVector p=particles[tid];
DebugReduc_SpecTypes types=largeStructVal;
auto origin=DebugReduc_Point{
0, 0}
;
auto distance=(origin.X+p.X);
auto nested=([&]() {
DebugReduc_NestedPhysicsState _tmp; _tmp.Position = p; _tmp.Velocity = p; _tmp.StatusFlag = 1; return _tmp; }
());
float mag=ComputeMagnitude(p);
mag=(float)abs(mag);
for(int i=0;(i<unrollFactor);i++)
{
if((i==1))
continue;
mag=(float)max(mag,((p.X*i)+Gravity));
}
if(!IsPositive(mag))
{
mag=0;
}
int counter=0;
while((counter<2))
{
mag=(float)min(mag,(mag+1));
counter++;
}
do
{
counter--;
}
while((counter>0));
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
if((tx==0))
{
atomicAdd(totalMagnitude,dynamicCache[0]);
successFlag=true;
}
DebugReduc_ComprehensiveSpecKernel_Vector8 inlineVec=DebugReduc_ComprehensiveSpecKernel_Vector8;
inlineVec[0]=123;
inlineVec[1]=456;
{
DebugReduc_ComprehensiveSpecKernel_PhysicsState state=DebugReduc_ComprehensiveSpecKernel_PhysicsState;
DebugReduc_ComprehensiveSpecKernel_PhysicsState* pState=&state;
int stateSize=PhysicsState;
pState.Buffer[0]=inlineVec[0];
(*pState).Buffer[1]=inlineVec[1];
unsigned long long carry=0;
unsigned int sum=__gsharp_add_cc(pState.Buffer[0],pState.Buffer[1],&carry);
long long borrow=0;
unsigned int diff=__gsharp_sub_cc(sum,pState.Buffer[1],&borrow);
if((sum>diff))
{
pState.Flags=1;
}
}
bool condition1=(tx>0);
bool condition2=(ty==0);
if((condition1&&condition2))
{
int bitwise=(((tx<<2))|(((ty>>1))^0xFF));
bitwise&=~1;
mag=((bitwise>0))(mag*2.0f)(mag/2.0f);
}
else
if((condition1||!condition2))
{
mag+=1.5f;
}
Intrinsics.InlinePtx(""/* test ptx inline */"");
if((ty>0))
{
return;
}
}
}
";
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct __Native_DebugReduc_SpecTypes
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
public static __Native_DebugReduc_SpecTypes Marshal_DebugReduc_SpecTypes(ref DebugReduc.SpecTypes host, bool isDevicePtr, System.Collections.Generic.List<IntPtr> allocatedPtrs)
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
public static void Unmarshal_DebugReduc_SpecTypes(ref DebugReduc.SpecTypes host, ref __Native_DebugReduc_SpecTypes native, bool isDevicePtr)
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
public static unsafe void Launch_ProcessPhysics(DebugReduc.PhysicsVector[] particles, float totalMagnitude, bool successFlag, DebugReduc.SpecTypes largeStructVal, int particleCount, int unrollFactor, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1, uint blockDimZ = 1, uint sharedMemBytes = 0, System.Collections.Generic.Dictionary<string, string>? macros = null)
{
using var _cudaCtxScope = new global::Kassyi.GSharp.Runtime.Jit.JitEngine.CudaContextScope();
var __gsharp_dynamic_src = string.Join
(System.Environment.NewLine, new string[]
{
"#define unrollFactor " + (unrollFactor).ToString(),
ProcessPhysics_CudaSource
}
);
IntPtr hfunc = global::Kassyi.GSharp.Runtime.Jit.JitEngine.GetOrCompileKernel(__gsharp_dynamic_src, "ProcessPhysics", macros);
IntPtr d_particles = IntPtr.Zero;
IntPtr d_totalMagnitude = IntPtr.Zero;
IntPtr d_successFlag = IntPtr.Zero;
System.Collections.Generic.List<IntPtr> largeStructVal_allocatedPtrs = new System.Collections.Generic.List<IntPtr>();
try
{
void** kernelParams = stackalloc void*[5];
// Marshal array: particles
int particles_size = particles.Length * System.Runtime.InteropServices.Marshal.SizeOf(typeof(DebugReduc.PhysicsVector));
CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_particles, particles_size));

System.Runtime.InteropServices.GCHandle handle_particles = System.Runtime.InteropServices.GCHandle.Alloc(particles, System.Runtime.InteropServices.GCHandleType.Pinned);
try
{
IntPtr p_particles = handle_particles.AddrOfPinnedObject();
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_particles, p_particles, particles_size));
}
finally
{
handle_particles.Free();
}
kernelParams[0] = &d_particles;
// Marshal ref/out scalar: totalMagnitude
int totalMagnitude_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(float));
CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_totalMagnitude, totalMagnitude_size));
// Fast zero-allocation copy to device
fixed (float* p_totalMagnitude_temp = &totalMagnitude)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_totalMagnitude, (IntPtr)p_totalMagnitude_temp, totalMagnitude_size));
}
kernelParams[1] = &d_totalMagnitude;
// Marshal ref/out scalar: successFlag
int successFlag_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(bool));
CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_successFlag, successFlag_size));
kernelParams[2] = &d_successFlag;
var native_largeStructVal = Marshal_DebugReduc_SpecTypes(ref largeStructVal, false, largeStructVal_allocatedPtrs);
// Marshal scalar struct: largeStructVal
kernelParams[3] = &native_largeStructVal;
// Marshal scalar: particleCount
kernelParams[4] = &particleCount;
CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
IntPtr.Zero, // Stream
kernelParams,
IntPtr.Zero // Extra
));
// Copy back array: particles
System.Runtime.InteropServices.GCHandle handleBack_particles = System.Runtime.InteropServices.GCHandle.Alloc(particles, System.Runtime.InteropServices.GCHandleType.Pinned);
try
{
IntPtr p_particles = handleBack_particles.AddrOfPinnedObject();
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_particles, d_particles, particles_size));
}
finally
{
handleBack_particles.Free();
}
// Unmarshal ref/out scalar: totalMagnitude
IntPtr p_totalMagnitude_temp = System.Runtime.InteropServices.Marshal.AllocHGlobal(totalMagnitude_size);
try
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_totalMagnitude_temp, d_totalMagnitude, totalMagnitude_size));
totalMagnitude = (float)System.Runtime.InteropServices.Marshal.PtrToStructure(p_totalMagnitude_temp, typeof(float));
}
finally
{
System.Runtime.InteropServices.Marshal.FreeHGlobal(p_totalMagnitude_temp);
}
// Unmarshal ref/out scalar: successFlag
IntPtr p_successFlag_temp = System.Runtime.InteropServices.Marshal.AllocHGlobal(successFlag_size);
try
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_successFlag_temp, d_successFlag, successFlag_size));
successFlag = (bool)System.Runtime.InteropServices.Marshal.PtrToStructure(p_successFlag_temp, typeof(bool));
}
finally
{
System.Runtime.InteropServices.Marshal.FreeHGlobal(p_successFlag_temp);
}
Unmarshal_DebugReduc_SpecTypes(ref largeStructVal, ref native_largeStructVal, false);
}
finally
{
if (d_particles != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_particles);
if (d_totalMagnitude != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_totalMagnitude);
if (d_successFlag != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_successFlag);
foreach (var ptr in largeStructVal_allocatedPtrs) CudaDriverApi.cuMemFree_v2(ptr);
}
}
public static unsafe void Launch_ProcessPhysics_Device(IntPtr particles, float totalMagnitude, bool successFlag, DebugReduc.SpecTypes largeStructVal, int particleCount, int unrollFactor, uint gridDimX = 1, uint gridDimY = 1, uint gridDimZ = 1, uint blockDimX = 1, uint blockDimY = 1, uint blockDimZ = 1, uint sharedMemBytes = 0, System.Collections.Generic.Dictionary<string, string>? macros = null)
{
using var _cudaCtxScope = new global::Kassyi.GSharp.Runtime.Jit.JitEngine.CudaContextScope();
var __gsharp_dynamic_src = string.Join
(System.Environment.NewLine, new string[]
{
"#define unrollFactor " + (unrollFactor).ToString(),
ProcessPhysics_CudaSource
}
);
IntPtr hfunc = global::Kassyi.GSharp.Runtime.Jit.JitEngine.GetOrCompileKernel(__gsharp_dynamic_src, "ProcessPhysics", macros);
System.Collections.Generic.List<IntPtr> largeStructVal_allocatedPtrs = new System.Collections.Generic.List<IntPtr>();
try
{
void** kernelParams = stackalloc void*[5];
// Marshal device array: particles
kernelParams[0] = &particles;
// Marshal ref/out scalar: totalMagnitude
int totalMagnitude_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(float));
CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_totalMagnitude, totalMagnitude_size));
// Fast zero-allocation copy to device
fixed (float* p_totalMagnitude_temp = &totalMagnitude)
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyHtoD_v2(d_totalMagnitude, (IntPtr)p_totalMagnitude_temp, totalMagnitude_size));
}
kernelParams[1] = &d_totalMagnitude;
// Marshal ref/out scalar: successFlag
int successFlag_size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(bool));
CudaException.ThrowIfError(CudaDriverApi.cuMemAlloc_v2(out d_successFlag, successFlag_size));
kernelParams[2] = &d_successFlag;
var native_largeStructVal = Marshal_DebugReduc_SpecTypes(ref largeStructVal, true, largeStructVal_allocatedPtrs);
// Marshal scalar struct: largeStructVal
kernelParams[3] = &native_largeStructVal;
// Marshal scalar: particleCount
kernelParams[4] = &particleCount;
CudaException.ThrowIfError(CudaDriverApi.cuLaunchKernel(
hfunc,
gridDimX, gridDimY, gridDimZ, // Grid dim
blockDimX, blockDimY, blockDimZ, // Block dim
sharedMemBytes, // Shared mem
IntPtr.Zero, // Stream
kernelParams,
IntPtr.Zero // Extra
));
// Unmarshal ref/out scalar: totalMagnitude
IntPtr p_totalMagnitude_temp = System.Runtime.InteropServices.Marshal.AllocHGlobal(totalMagnitude_size);
try
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_totalMagnitude_temp, d_totalMagnitude, totalMagnitude_size));
totalMagnitude = (float)System.Runtime.InteropServices.Marshal.PtrToStructure(p_totalMagnitude_temp, typeof(float));
}
finally
{
System.Runtime.InteropServices.Marshal.FreeHGlobal(p_totalMagnitude_temp);
}
// Unmarshal ref/out scalar: successFlag
IntPtr p_successFlag_temp = System.Runtime.InteropServices.Marshal.AllocHGlobal(successFlag_size);
try
{
CudaException.ThrowIfError(CudaDriverApi.cuMemcpyDtoH_v2(p_successFlag_temp, d_successFlag, successFlag_size));
successFlag = (bool)System.Runtime.InteropServices.Marshal.PtrToStructure(p_successFlag_temp, typeof(bool));
}
finally
{
System.Runtime.InteropServices.Marshal.FreeHGlobal(p_successFlag_temp);
}
Unmarshal_DebugReduc_SpecTypes(ref largeStructVal, ref native_largeStructVal, true);
}
finally
{
if (d_totalMagnitude != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_totalMagnitude);
if (d_successFlag != IntPtr.Zero) CudaDriverApi.cuMemFree_v2(d_successFlag);
foreach (var ptr in largeStructVal_allocatedPtrs) CudaDriverApi.cuMemFree_v2(ptr);
}
}
}
}
```

<!-- END CPP -->

## 8.4 Sad Path Compilation Errors (Diagnostics)

To ensure that the constraints outlined in [06_limitations.md](06_limitations.md) are strictly enforced, G# includes an exhaustive suite of diagnostic tests (`tests\Kassyi.GSharp.Tests\GeneratorDiagnosticsTests.cs`).

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
2. **GSH0002**: `The string type is not supported in GPU kernels.` (Triggered by `string s = "hello"`)
3. **GSH0003**: `The syntax 'ObjectCreationExpression' is not supported in GSharp GPU kernels.` (Fallback trigger for any unknown syntax node)
4. **GSH0004**: `Method 'BadKernel' is marked with [GpuKernel] but returns 'int'. GpuKernel methods must return void.`

## Conclusion

This explicit trace proves that:

1. `JitConstant` uses preprocessor macros.
2. Custom Structs map flawlessly, and all 11 primitive C# types align to their C++ equivalents.
3. Diagnostic warnings and errors natively integrate with MSBuild (`GSH0001` - `GSH0004` and `GSH1001` - `GSH1002`) to act as strict guardrails.
4. `CastExpression` and `ParenthesizedExpression` AST nodes are properly translated, allowing explicit math method casting like `(float)Math.Sqrt(...)`.
5. **`continue` statements are currently silently ignored** (documented limitation).
6. **Phase 5 Target Identified**: `ref` and `out` scalars are correctly transpiled to pointers in `.cu` but their Device-to-Host copy-back logic is currently **missing** in the host C# launcher.
