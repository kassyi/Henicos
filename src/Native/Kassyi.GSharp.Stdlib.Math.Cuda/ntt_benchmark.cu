// GSharp Prelude Header
// Embedded intrinsic functions and standard type definitions for GSharp-generated CUDA C++ code.

#include <cooperative_groups.h>
namespace cg = cooperative_groups;

/**
 * @brief CUDA device-side representation of C# Nullable<T> (T?).
 */
template <typename T>
struct System_Nullable {
    bool HasValue;
    T Value;
    __device__ System_Nullable() : HasValue(false), Value() {}
    __device__ System_Nullable(T val) : HasValue(true), Value(val) {}
};

/**
 * @brief CUDA device-side representation of C# Array / Span fat pointer.
 */
template <typename T>
struct GSharpArray {
    T* __restrict__ data;
    int length;

    __device__ inline T* begin() { return data; }
    __device__ inline T* end() { return data + length; }
    __device__ inline const T* begin() const { return data; }
    __device__ inline const T* end() const { return data + length; }
    __device__ inline int size() const { return length; }
    __device__ inline T& operator[](int index) { return data[index]; }
    __device__ inline const T& operator[](int index) const { return data[index]; }
    __device__ inline operator T*() { return data; }
    __device__ inline operator const T*() const { return data; }
};

/**
 * @brief 32-bit unsigned addition with carry-in and carry-out (PTX add.cc / addc).
 * @param a First operand.
 * @param b Second operand.
 * @param carry Pointer to carry-in flag (updated in-place to carry-out flag).
 * @return Sum of a + b + carry_in (mod 2^32).
 */
__device__ __forceinline__ unsigned int __gsharp_add_cc(unsigned int a, unsigned int b, unsigned long long* carry) {
    unsigned int res;
    unsigned int c_in = (unsigned int)*carry;
    unsigned int c_out;
    asm volatile(
        "add.cc.u32 %0, %2, -1;\n\t"
        "addc.cc.u32 %0, %3, %4;\n\t"
        "addc.u32 %1, 0, 0;"
        : "=&r"(res), "=r"(c_out)
        : "r"(c_in), "r"(a), "r"(b)
    );
    *carry = c_out;
    return res;
}

/**
 * @brief 32-bit unsigned subtraction with borrow-in and borrow-out (PTX sub.cc / subc).
 * @param a Minuend operand.
 * @param b Subtrahend operand.
 * @param borrow Pointer to borrow-in flag (updated in-place to borrow-out flag).
 * @return Difference of a - b - borrow_in (mod 2^32).
 */
__device__ __forceinline__ unsigned int __gsharp_sub_cc(unsigned int a, unsigned int b, long long* borrow) {
    unsigned int res;
    unsigned int dummy;
    unsigned int b_in = (unsigned int)*borrow;
    unsigned int b_out;
    asm volatile(
        "sub.cc.u32 %1, 0, %3;\n\t"
        "subc.cc.u32 %0, %4, %5;\n\t"
        "subc.u32 %2, 0, 0;"
        : "=&r"(res), "=&r"(dummy), "=r"(b_out)
        : "r"(b_in), "r"(a), "r"(b)
    );
    *borrow = b_out & 1;
    return res;
}

/**
 * @brief 32-bit unsigned multiplication widening to 64-bit result (PTX mul.wide.u32).
 */
__device__ __forceinline__ unsigned long long __gsharp_mul_wide(unsigned int a, unsigned int b) {
    unsigned long long res;
    asm volatile("mul.wide.u32 %0, %1, %2;" : "=l"(res) : "r"(a), "r"(b));
    return res;
}

/**
 * @brief 32-bit circular bitwise left rotation (BitOperations.RotateLeft).
 */
__device__ __forceinline__ unsigned int __gsharp_rotl32(unsigned int value, int offset) {
    offset &= 31;
    return __funnelshift_l(value, value, offset);
}

/**
 * @brief 32-bit circular bitwise right rotation (BitOperations.RotateRight).
 */
__device__ __forceinline__ unsigned int __gsharp_rotr32(unsigned int value, int offset) {
    offset &= 31;
    return __funnelshift_r(value, value, offset);
}

/**
 * @brief 64-bit circular bitwise left rotation (BitOperations.RotateLeft).
 */
__device__ __forceinline__ unsigned long long __gsharp_rotl64(unsigned long long value, int offset) {
    offset &= 63;
    return offset ? (value << offset) | (value >> (64 - offset)) : value;
}

/**
 * @brief 64-bit circular bitwise right rotation (BitOperations.RotateRight).
 */
__device__ __forceinline__ unsigned long long __gsharp_rotr64(unsigned long long value, int offset) {
    offset &= 63;
    return offset ? (value >> offset) | (value << (64 - offset)) : value;
}

/**
 * @brief 32-bit trailing zero count (BitOperations.TrailingZeroCount).
 */
__device__ __forceinline__ int __gsharp_tzc_32(unsigned int value) {
    return value ? __ffs(value) - 1 : 32;
}

/**
 * @brief 64-bit trailing zero count (BitOperations.TrailingZeroCount).
 */
__device__ __forceinline__ int __gsharp_tzc_64(unsigned long long value) {
    return value ? __ffsll(value) - 1 : 64;
}

/**
 * @brief 32-bit integer base-2 logarithm (BitOperations.Log2).
 */
__device__ __forceinline__ int __gsharp_log2_32(unsigned int value) {
    return value ? 31 - __clz(value) : 0;
}

/**
 * @brief 64-bit integer base-2 logarithm (BitOperations.Log2).
 */
__device__ __forceinline__ int __gsharp_log2_64(unsigned long long value) {
    return value ? 63 - __clzll(value) : 0;
}

/**
 * @brief 32-bit leading zero count (BitOperations.LeadingZeroCount).
 */
__device__ __forceinline__ int __gsharp_lzc_32(unsigned int value) {
    return value ? __clz(value) : 32;
}

/**
 * @brief 64-bit leading zero count (BitOperations.LeadingZeroCount).
 */
__device__ __forceinline__ int __gsharp_lzc_64(unsigned long long value) {
    return value ? __clzll(value) : 64;
}

__forceinline__ __device__ int __gsharp_clamp(int value, int min_val, int max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ unsigned int __gsharp_clamp(unsigned int value, unsigned int min_val, unsigned int max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ float __gsharp_clamp(float value, float min_val, float max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }
__forceinline__ __device__ double __gsharp_clamp(double value, double min_val, double max_val) { return value < min_val ? min_val : (value > max_val ? max_val : value); }

__forceinline__ __device__ int __gsharp_atomicCAS(int* address, int val, int compare) { return atomicCAS(address, compare, val); }
__forceinline__ __device__ unsigned int __gsharp_atomicCAS(unsigned int* address, unsigned int val, unsigned int compare) { return atomicCAS(address, compare, val); }
__forceinline__ __device__ unsigned long long __gsharp_atomicCAS(unsigned long long* address, unsigned long long val, unsigned long long compare) { return atomicCAS(address, compare, val); }

__forceinline__ __device__ int __gsharp_atomicAdd(int* address, int val) { return atomicAdd(address, val) + val; }
__forceinline__ __device__ unsigned int __gsharp_atomicAdd(unsigned int* address, unsigned int val) { return atomicAdd(address, val) + val; }
__forceinline__ __device__ unsigned long long __gsharp_atomicAdd(unsigned long long* address, unsigned long long val) { return atomicAdd(address, val) + val; }

__forceinline__ __device__ unsigned long long Device_ModPow64_Under32Bit(unsigned long long baseVal, unsigned long long exp, unsigned long long m);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_IsZero(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_IsOne(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_CompareTo(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx, unsigned long long* dResult);

inline __device__ void Device_Unpack16(const unsigned int* dSrc, unsigned int* dDst1, unsigned int* dDst2, int n, int numWords);

inline __device__ void Device_Transpose_32x32(const unsigned int* idata, unsigned int* odata, int width, int height);

inline __device__ int GetBitLength_Device(unsigned int* data, int numWords);

inline __device__ bool IsZero_Device(unsigned int* data, int numWords);

inline __device__ int Compare_Device(unsigned int* a, unsigned int* b, int numWords);

inline __device__ void Swap_Device(unsigned int* a, unsigned int* b, int numWords);

inline __device__ void Sub_Device(unsigned int* a, unsigned int* b, int numWords);

inline __device__ void Add_Device(unsigned int* a, unsigned int* b, int numWords);

inline __device__ void ShiftRight1_Device(unsigned int* a, int numWords);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_ChunkAdd(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext* __restrict__  ctx, int chunkSize);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_ApplyAddCarry(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext* __restrict__  ctx, int numChunks, int chunkSize);

inline __device__ void AtomicAddWithCarry(unsigned int* address, int wordIndex, unsigned int valueToAdd, int maxWordCount);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_ChunkSub(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext* __restrict__  ctx, int chunkSize);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_ApplySubBorrow(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext* __restrict__  ctx, int numChunks, int chunkSize);

inline __device__ void AtomicSubWithBorrow(unsigned int* address, int wordIndex, unsigned int valueToSubtract, int maxWordCount);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_ShiftLeft(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx, int wordShift, int bitShift);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_ShiftRight(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx, int wordShift, int bitShift);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_BitwiseAnd(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_BitwiseOr(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_BitwiseXor(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx);

struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext;
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext;
inline __device__ void Device_BitwiseNot(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx);

inline __device__ unsigned int ModAdd(unsigned int operandA, unsigned int operandB, unsigned int primeModulus);

inline __device__ unsigned int ModSub(unsigned int operandA, unsigned int operandB, unsigned int primeModulus);

inline __device__ unsigned int ModMulBarrett(unsigned int operandA, unsigned int operandB, unsigned int primeModulus, unsigned long long barrettMagic);

inline __device__ unsigned int ModMul1(unsigned int operandA, unsigned int operandB);

inline __device__ unsigned int ModMul2(unsigned int operandA, unsigned int operandB);

inline __device__ unsigned int ModPow(unsigned int baseValue, unsigned int exponent, unsigned int primeModulus, unsigned long long barrettMagic);

struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs;
inline __device__ void Device_NTT_Shared_Row(Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs args);

__forceinline__ __device__ void Device_NTT_LoadAndTwiddleBefore(int rowIndex, int threadId, int currentLength, int maxLog2Length, int transformLength, bool applyTwiddleBefore, unsigned int primeModulus, unsigned long long barrettMagic, const unsigned int* deviceData, const unsigned int* deviceTwiddle);

__forceinline__ __device__ void Device_NTT_ButterflyStep(int step, int threadId, int transformLength, unsigned int primeModulus, unsigned long long barrettMagic, const unsigned int* deviceTwiddle);

__forceinline__ __device__ void Device_NTT_TwiddleAfterAndStore(int rowIndex, int threadId, int currentLength, int transformLength, bool applyTwiddleAfter, unsigned int primeModulus, unsigned long long barrettMagic, unsigned int* deviceData, const unsigned int* deviceTwiddle);

inline __device__ void Device_NTT_PointwiseSquare(unsigned int* deviceData1, unsigned int* deviceData2, int transformLength);

inline __device__ void Device_NTT_PointwiseMultiply(unsigned int* deviceData1A, unsigned int* deviceData2A, unsigned int* deviceData1B, unsigned int* deviceData2B, int transformLength);

inline __device__ void Device_CRT_ComputeCoefficients(const unsigned int* deviceResult1, const unsigned int* deviceResult2, unsigned int* uncarriedLow, unsigned int* uncarriedHigh, int transformLength);

struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs;
inline __device__ void Device_NTT_Internal(Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs args);

struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs;
inline __device__ void Device_ForwardNTT(Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs args);

struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs;
inline __device__ void Device_InverseNTT(Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs args);

inline __device__ void Device_ScalarAdd(unsigned int* left, unsigned int s, unsigned int* result, int numWords);

inline __device__ void Device_ScalarSub(unsigned int* left, unsigned int s, unsigned int* result, int numWords);

inline __device__ void Device_ScalarMul(unsigned int* left, unsigned int s, unsigned int* result, int numWords);

inline __device__ void Device_ChunkScalarMul(unsigned int* left, unsigned int s, unsigned int* result, long long* carryBuf, int numWords, int chunkSize);

__forceinline__ __device__ unsigned long long Device_ModPow64_Under32Bit(unsigned long long baseVal, unsigned long long exp, unsigned long long m){
unsigned long long res=1;
baseVal%=m;
while(exp>0)
{
if((exp&1)!=0)
{
res=(res*baseVal)%m;
}
baseVal=(baseVal*baseVal)%m;
exp>>=1;
}
return res;
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_IsZero(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
if(ctx.Operands.LeftOperand[i]!=0)
{
atomicOr(&ctx.Operands.ResultBuffer[0],1);
}
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_IsOne(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
if(i==0)
{
if(ctx.Operands.LeftOperand[0]!=1)
{
atomicOr(&ctx.Operands.ResultBuffer[0],1);
}
}
else
{
if(ctx.Operands.LeftOperand[i]!=0)
{
atomicOr(&ctx.Operands.ResultBuffer[0],1);
}
}
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_CompareTo(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx, unsigned long long* dResult){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
auto wa=ctx.Operands.LeftOperand[i];
auto wb=ctx.Operands.RightOperand[i];
if(wa!=wb)
{
long long diff=(long long)wa-(long long)wb;
auto signMask=(unsigned long long)(diff>>63);
auto resultBits=2ul-(signMask&1ul);
auto val=((unsigned long long)i<<32)|resultBits;
atomicMax(&dResult[0],val);
}
}
}

inline __device__ void Device_Unpack16(const unsigned int* dSrc, unsigned int* dDst1, unsigned int* dDst2, int n, int numWords){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < n; i += gridDim.x * blockDim.x)
{
unsigned int val=0;
auto wordIdx=i/2;
if(wordIdx<numWords)
{
auto w=dSrc[wordIdx];
if(i%2==0)
{
val=w&0xFFFF;
}
else
{
val=w>>16;
}
}
dDst1[i]=val;
dDst2[i]=val;
}
}

inline __device__ void Device_Transpose_32x32(const unsigned int* idata, unsigned int* odata, int width, int height){
extern __shared__ uint32_t s_mem[];
;
auto gridX=(width+31)/32;
auto gridY=(height+31)/32;
if(blockIdx.x>=gridX*gridY)
{
return;
}
auto bx=blockIdx.x%gridX;
auto by=blockIdx.x/gridX;
if(threadIdx.x>=256)
{
return;
}
auto tx=threadIdx.x%32;
auto ty=threadIdx.x/32;
auto xIndex=bx*32+tx;
auto yIndex=by*32+ty;
// [GpuUnroll] start=0, count=4
{
auto i=0*8;
if(xIndex<width&&yIndex+i<height)
{
s_mem[(ty + i) * 33 + tx] = idata[(yIndex+i)*width+xIndex];
;
}
}
{
auto i=1*8;
if(xIndex<width&&yIndex+i<height)
{
s_mem[(ty + i) * 33 + tx] = idata[(yIndex+i)*width+xIndex];
;
}
}
{
auto i=2*8;
if(xIndex<width&&yIndex+i<height)
{
s_mem[(ty + i) * 33 + tx] = idata[(yIndex+i)*width+xIndex];
;
}
}
{
auto i=3*8;
if(xIndex<width&&yIndex+i<height)
{
s_mem[(ty + i) * 33 + tx] = idata[(yIndex+i)*width+xIndex];
;
}
}
__syncthreads();
xIndex=by*32+tx;
yIndex=bx*32+ty;
// [GpuUnroll] start=0, count=4
{
auto i=0*8;
if(xIndex<height&&yIndex+i<width)
{
unsigned int val=0;
val = s_mem[tx * 33 + (ty + i)];
;
odata[(yIndex+i)*height+xIndex]=val;
}
}
{
auto i=1*8;
if(xIndex<height&&yIndex+i<width)
{
unsigned int val=0;
val = s_mem[tx * 33 + (ty + i)];
;
odata[(yIndex+i)*height+xIndex]=val;
}
}
{
auto i=2*8;
if(xIndex<height&&yIndex+i<width)
{
unsigned int val=0;
val = s_mem[tx * 33 + (ty + i)];
;
odata[(yIndex+i)*height+xIndex]=val;
}
}
{
auto i=3*8;
if(xIndex<height&&yIndex+i<width)
{
unsigned int val=0;
val = s_mem[tx * 33 + (ty + i)];
;
odata[(yIndex+i)*height+xIndex]=val;
}
}
}

inline __device__ int GetBitLength_Device(unsigned int* data, int numWords){
for(int i=numWords-1;i>=0;i--)
{
auto w=data[i];
if(w!=0)
{
auto bitLen=i*32;
while(w>0)
{
bitLen++;
w>>=1;
}
return bitLen;
}
}
return 0;
}

inline __device__ bool IsZero_Device(unsigned int* data, int numWords){
for(int i=0;i<numWords;i++)
{
if(data[i]!=0)
{
return false;
}
}
return true;
}

inline __device__ int Compare_Device(unsigned int* a, unsigned int* b, int numWords){
for(int i=numWords-1;i>=0;i--)
{
if(a[i]>b[i])
{
return 1;
}
if(a[i]<b[i])
{
return -1;
}
}
return 0;
}

inline __device__ void Swap_Device(unsigned int* a, unsigned int* b, int numWords){
for(int i=0;i<numWords;i++)
{
auto t=a[i];
a[i]=b[i];
b[i]=t;
}
}

inline __device__ void Sub_Device(unsigned int* a, unsigned int* b, int numWords){
long long borrow=0;
for(int i=0;i<numWords;i++)
{
auto diff=(long long)a[i]-b[i]+borrow;
a[i]=(unsigned int)(diff&0xFFFFFFFF);
borrow=diff>>32;
}
}

inline __device__ void Add_Device(unsigned int* a, unsigned int* b, int numWords){
long long carry=0;
for(int i=0;i<numWords;i++)
{
auto sum=(long long)a[i]+b[i]+carry;
a[i]=(unsigned int)(sum&0xFFFFFFFF);
carry=sum>>32;
}
}

inline __device__ void ShiftRight1_Device(unsigned int* a, int numWords){
unsigned int carry=0;
for(int i=numWords-1;i>=0;i--)
{
auto nextCarry=(a[i]&1)<<31;
a[i]=(a[i]>>1)|carry;
carry=nextCarry;
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_ChunkAdd(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext* __restrict__  ctx, int chunkSize){
auto chunkIdx=blockIdx.x*blockDim.x+threadIdx.x;
auto start=chunkIdx*chunkSize;
if(start>=(*ctx).NumberOfWords)
{
return;
}
auto end=start+chunkSize;
if(end>(*ctx).NumberOfWords)
{
end=(*ctx).NumberOfWords;
}
unsigned long long localCarry=0;
for(auto i=start;i<end;i++)
{
(*ctx).Operands.ResultBuffer[i]=__gsharp_add_cc((*ctx).Operands.LeftOperand[i],(*ctx).Operands.RightOperand[i],&localCarry);
}
(*ctx).Operands.CarryBuffer[chunkIdx+1]=(long long)localCarry;
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_ApplyAddCarry(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext* __restrict__  ctx, int numChunks, int chunkSize){
auto cIdx=blockIdx.x*blockDim.x+threadIdx.x;
if(cIdx<=0||cIdx>numChunks)
{
return;
}
auto carry=(*ctx).Operands.CarryBuffer[cIdx];
if(carry>0)
{
AtomicAddWithCarry((*ctx).Operands.ResultBuffer,cIdx*chunkSize,(unsigned int)carry,(*ctx).NumberOfWords);
}
}

inline __device__ void AtomicAddWithCarry(unsigned int* address, int wordIndex, unsigned int valueToAdd, int maxWordCount){
auto carry=valueToAdd;
auto index=wordIndex;
while(carry>0&&index<maxWordCount)
{
auto oldValue=atomicAdd(&address[index],carry);
if(oldValue+carry<oldValue)
{
carry=1;
index++;
}
else
{
break;
}
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_ChunkSub(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext* __restrict__  ctx, int chunkSize){
auto chunkIdx=blockIdx.x*blockDim.x+threadIdx.x;
auto start=chunkIdx*chunkSize;
if(start>=(*ctx).NumberOfWords)
{
return;
}
auto end=start+chunkSize;
if(end>(*ctx).NumberOfWords)
{
end=(*ctx).NumberOfWords;
}
long long localBorrow=0;
for(auto i=start;i<end;i++)
{
(*ctx).Operands.ResultBuffer[i]=__gsharp_sub_cc((*ctx).Operands.LeftOperand[i],(*ctx).Operands.RightOperand[i],&localBorrow);
}
(*ctx).Operands.CarryBuffer[chunkIdx+1]=localBorrow;
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_ApplySubBorrow(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext* __restrict__  ctx, int numChunks, int chunkSize){
auto cIdx=blockIdx.x*blockDim.x+threadIdx.x;
if(cIdx<=0||cIdx>numChunks)
{
return;
}
auto borrow=(*ctx).Operands.CarryBuffer[cIdx];
if(borrow>0)
{
AtomicSubWithBorrow((*ctx).Operands.ResultBuffer,cIdx*chunkSize,(unsigned int)borrow,(*ctx).NumberOfWords);
}
}

inline __device__ void AtomicSubWithBorrow(unsigned int* address, int wordIndex, unsigned int valueToSubtract, int maxWordCount){
auto borrow=valueToSubtract;
auto index=wordIndex;
while(borrow>0&&index<maxWordCount)
{
auto oldValue=atomicSub(&address[index],borrow);
if(oldValue<borrow)
{
borrow=1;
index++;
}
else
{
break;
}
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_ShiftLeft(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx, int wordShift, int bitShift){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
auto srcIdx0=i-wordShift;
auto srcIdx1=srcIdx0-1;
unsigned int w0=0;
unsigned int w1=0;
if(srcIdx0>=0&&srcIdx0<ctx.NumberOfWords)
{
w0=ctx.Operands.LeftOperand[srcIdx0];
}
if(srcIdx1>=0&&srcIdx1<ctx.NumberOfWords)
{
w1=ctx.Operands.LeftOperand[srcIdx1];
}
if(bitShift==0)
{
ctx.Operands.ResultBuffer[i]=w0;
}
else
{
ctx.Operands.ResultBuffer[i]=(w0<<bitShift)|(w1>>(32-bitShift));
}
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_ShiftRight(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx, int wordShift, int bitShift){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
auto srcIdx0=i+wordShift;
auto srcIdx1=srcIdx0+1;
unsigned int w0=0;
unsigned int w1=0;
if(srcIdx0>=0&&srcIdx0<ctx.NumberOfWords)
{
w0=ctx.Operands.LeftOperand[srcIdx0];
}
if(srcIdx1>=0&&srcIdx1<ctx.NumberOfWords)
{
w1=ctx.Operands.LeftOperand[srcIdx1];
}
if(bitShift==0)
{
ctx.Operands.ResultBuffer[i]=w0;
}
else
{
ctx.Operands.ResultBuffer[i]=(w0>>bitShift)|(w1<<(32-bitShift));
}
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_BitwiseAnd(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
ctx.Operands.ResultBuffer[i]=ctx.Operands.LeftOperand[i]&ctx.Operands.RightOperand[i];
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_BitwiseOr(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
ctx.Operands.ResultBuffer[i]=ctx.Operands.LeftOperand[i]|ctx.Operands.RightOperand[i];
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_BitwiseXor(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
ctx.Operands.ResultBuffer[i]=ctx.Operands.LeftOperand[i]^ctx.Operands.RightOperand[i];
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext
{
const unsigned int* LeftOperand;
const unsigned int* RightOperand;
unsigned int* ResultBuffer;
long long* CarryBuffer;
}
;
#endif
#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext__
struct Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext
{
Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext_OperandsContext Operands;
int NumberOfWords;
}
;
#endif
inline __device__ void Device_BitwiseNot(Kassyi_GSharp_Stdlib_Math_Contexts_BigIntKernelContext ctx){
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < ctx.NumberOfWords; i += gridDim.x * blockDim.x)
{
ctx.Operands.ResultBuffer[i]=~ctx.Operands.LeftOperand[i];
}
}

inline __device__ unsigned int ModAdd(unsigned int operandA, unsigned int operandB, unsigned int primeModulus){
if(operandA>=primeModulus-operandB)
{
return operandA-(primeModulus-operandB);
}
return operandA+operandB;
}

inline __device__ unsigned int ModSub(unsigned int operandA, unsigned int operandB, unsigned int primeModulus){
auto result=operandA-operandB;
if(operandA<operandB)
{
result+=primeModulus;
}
return result;
}

inline __device__ unsigned int ModMulBarrett(unsigned int operandA, unsigned int operandB, unsigned int primeModulus, unsigned long long barrettMagic){
unsigned long long product=0;
asm volatile("mul.wide.u32 %0, %1, %2;" : "=l"(product) : "r"(operandA), "r"(operandB));
;
unsigned long long quotient=0;
asm volatile("mul.hi.u64 %0, %1, %2;" : "=l"(quotient) : "l"(product), "l"(barrettMagic));
;
auto remainder=product-quotient*primeModulus;
while(remainder>=primeModulus)
{
remainder-=primeModulus;
}
return (unsigned int)remainder;
}

inline __device__ unsigned int ModMul1(unsigned int operandA, unsigned int operandB){
return ModMulBarrett(operandA,operandB,4194304001U,4398046510ULL);
}

inline __device__ unsigned int ModMul2(unsigned int operandA, unsigned int operandB){
return ModMulBarrett(operandA,operandB,2717908993U,6787108810ULL);
}

inline __device__ unsigned int ModPow(unsigned int baseValue, unsigned int exponent, unsigned int primeModulus, unsigned long long barrettMagic){
unsigned int result=1;
baseValue=baseValue%primeModulus;
while(exponent>0)
{
if((exponent&1)!=0)
{
result=ModMulBarrett(result,baseValue,primeModulus,barrettMagic);
}
baseValue=ModMulBarrett(baseValue,baseValue,primeModulus,barrettMagic);
exponent>>=1;
}
return result;
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs
{
unsigned int* Buffer;
unsigned int* TemporaryBuffer;
const unsigned int* Twiddle;
int M;
int N1;
int N2;
unsigned int P;
unsigned long long Pm;
bool IsInverse;
bool ApplyTwiddleBefore;
bool ApplyTwiddleAfter;
int MaxBlocks;
int CurrentL;
}
;
#endif
inline __device__ void Device_NTT_Shared_Row(Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs args){
auto deviceData=args.Buffer;
auto deviceTwiddle=args.Twiddle;
auto transformLength=args.M;
auto currentLength=args.CurrentL;
auto primeModulus=args.P;
auto barrettMagic=args.Pm;
auto applyTwiddleBefore=args.ApplyTwiddleBefore;
auto applyTwiddleAfter=args.ApplyTwiddleAfter;
auto maxBlocks=args.MaxBlocks;
auto threadsNeeded=currentLength/2;
int maxLog2Length=0;
while((1<<maxLog2Length)<currentLength)
{
maxLog2Length++;
}
extern __shared__ uint32_t s_row[];
;
for(int rowIndex=blockIdx.x;rowIndex<maxBlocks;rowIndex+=gridDim.x)
{
for(int threadId=threadIdx.x;threadId<threadsNeeded;threadId+=blockDim.x)
{
Device_NTT_LoadAndTwiddleBefore(rowIndex,threadId,currentLength,maxLog2Length,transformLength,applyTwiddleBefore,primeModulus,barrettMagic,deviceData,deviceTwiddle);
}
__syncthreads();
for(int step=0;step<maxLog2Length;step++)
{
for(int threadId=threadIdx.x;threadId<threadsNeeded;threadId+=blockDim.x)
{
Device_NTT_ButterflyStep(step,threadId,transformLength,primeModulus,barrettMagic,deviceTwiddle);
}
__syncthreads();
}
for(int threadId=threadIdx.x;threadId<threadsNeeded;threadId+=blockDim.x)
{
Device_NTT_TwiddleAfterAndStore(rowIndex,threadId,currentLength,transformLength,applyTwiddleAfter,primeModulus,barrettMagic,deviceData,deviceTwiddle);
}
__syncthreads();
}
}

__forceinline__ __device__ void Device_NTT_LoadAndTwiddleBefore(int rowIndex, int threadId, int currentLength, int maxLog2Length, int transformLength, bool applyTwiddleBefore, unsigned int primeModulus, unsigned long long barrettMagic, const unsigned int* deviceData, const unsigned int* deviceTwiddle){
extern __shared__ uint32_t s_row[];
;
auto blockOffset=rowIndex*currentLength;
auto value1=deviceData[blockOffset+threadId];
auto value2=deviceData[blockOffset+threadId+currentLength/2];
unsigned int reversedThreadId1=__brev((unsigned int)threadId)>>(32-maxLog2Length);
unsigned int reversedThreadId2=__brev((unsigned int)(threadId+currentLength/2))>>(32-maxLog2Length);
if(applyTwiddleBefore)
{
auto twiddleIndex1=(unsigned long long)rowIndex*(unsigned long long)threadId;
twiddleIndex1%=(unsigned long long)transformLength;
value1=ModMulBarrett(value1,deviceTwiddle[twiddleIndex1],primeModulus,barrettMagic);
auto twiddleIndex2=(unsigned long long)rowIndex*(unsigned long long)(threadId+currentLength/2);
twiddleIndex2%=(unsigned long long)transformLength;
value2=ModMulBarrett(value2,deviceTwiddle[twiddleIndex2],primeModulus,barrettMagic);
}
s_row[reversedThreadId1] = value1;
;
s_row[reversedThreadId2] = value2;
;
}

__forceinline__ __device__ void Device_NTT_ButterflyStep(int step, int threadId, int transformLength, unsigned int primeModulus, unsigned long long barrettMagic, const unsigned int* deviceTwiddle){
extern __shared__ uint32_t s_row[];
;
auto length=2<<step;
auto halfLength=length/2;
auto blockIndex=threadId/halfLength;
auto elementIndex=threadId%halfLength;
auto position=blockIndex*length+elementIndex;
auto twiddleFactorIndex=elementIndex*(transformLength/length);
auto twiddleFactor=deviceTwiddle[twiddleFactorIndex];
unsigned int leftTerm=0;
leftTerm = s_row[position];
;
unsigned int rightTermSource=0;
rightTermSource = s_row[position+halfLength];
;
auto rightTerm=ModMulBarrett(rightTermSource,twiddleFactor,primeModulus,barrettMagic);
auto additionResult=ModAdd(leftTerm,rightTerm,primeModulus);
auto subtractionResult=ModSub(leftTerm,rightTerm,primeModulus);
s_row[position] = additionResult;
;
s_row[position+halfLength] = subtractionResult;
;
}

__forceinline__ __device__ void Device_NTT_TwiddleAfterAndStore(int rowIndex, int threadId, int currentLength, int transformLength, bool applyTwiddleAfter, unsigned int primeModulus, unsigned long long barrettMagic, unsigned int* deviceData, const unsigned int* deviceTwiddle){
extern __shared__ uint32_t s_row[];
;
auto blockOffset=rowIndex*currentLength;
if(applyTwiddleAfter)
{
auto twiddleIndex1=(unsigned long long)rowIndex*(unsigned long long)threadId;
twiddleIndex1%=(unsigned long long)transformLength;
unsigned int result1=0;
result1 = s_row[threadId];
;
result1=ModMulBarrett(result1,deviceTwiddle[twiddleIndex1],primeModulus,barrettMagic);
s_row[threadId] = result1;
;
auto twiddleIndex2=(unsigned long long)rowIndex*(unsigned long long)(threadId+currentLength/2);
twiddleIndex2%=(unsigned long long)transformLength;
unsigned int result2=0;
result2 = s_row[threadId+currentLength/2];
;
result2=ModMulBarrett(result2,deviceTwiddle[twiddleIndex2],primeModulus,barrettMagic);
s_row[threadId+currentLength/2] = result2;
;
}
unsigned int output1=0;
unsigned int output2=0;
output1 = s_row[threadId];
;
output2 = s_row[threadId+currentLength/2];
;
deviceData[blockOffset+threadId]=output1;
deviceData[blockOffset+threadId+currentLength/2]=output2;
}

inline __device__ void Device_NTT_PointwiseSquare(unsigned int* deviceData1, unsigned int* deviceData2, int transformLength){
for (int index = blockIdx.x * blockDim.x + threadIdx.x; index < transformLength; index += gridDim.x * blockDim.x)
{
deviceData1[index]=ModMulBarrett(deviceData1[index],deviceData1[index],4194304001U,4398046510ULL);
deviceData2[index]=ModMulBarrett(deviceData2[index],deviceData2[index],2717908993U,6787108810ULL);
}
}

inline __device__ void Device_NTT_PointwiseMultiply(unsigned int* deviceData1A, unsigned int* deviceData2A, unsigned int* deviceData1B, unsigned int* deviceData2B, int transformLength){
for (int index = blockIdx.x * blockDim.x + threadIdx.x; index < transformLength; index += gridDim.x * blockDim.x)
{
deviceData1A[index]=ModMulBarrett(deviceData1A[index],deviceData1B[index],4194304001U,4398046510ULL);
deviceData2A[index]=ModMulBarrett(deviceData2A[index],deviceData2B[index],2717908993U,6787108810ULL);
}
}

inline __device__ void Device_CRT_ComputeCoefficients(const unsigned int* deviceResult1, const unsigned int* deviceResult2, unsigned int* uncarriedLow, unsigned int* uncarriedHigh, int transformLength){
auto inverseN1=ModPow((unsigned int)transformLength,4194304001U-2,4194304001U,4398046510ULL);
auto inverseN2=ModPow((unsigned int)transformLength,2717908993U-2,2717908993U,6787108810ULL);
for (int index = blockIdx.x * blockDim.x + threadIdx.x; index < transformLength; index += gridDim.x * blockDim.x)
{
unsigned long long Modulus1=4194304001U;
unsigned long long Modulus2=2717908993U;
unsigned long long value1=ModMulBarrett(deviceResult1[index],inverseN1,4194304001U,4398046510ULL);
unsigned long long value2=ModMulBarrett(deviceResult2[index],inverseN2,2717908993U,6787108810ULL);
unsigned long long difference;
unsigned long long v1Mod2=value1%Modulus2;
if(value2>=v1Mod2)
{
difference=value2-v1Mod2;
}
else
{
difference=value2+Modulus2-v1Mod2;
}
unsigned long long Prime1InverseModPrime2=1050101200UL;
auto crtCoefficient=(difference*Prime1InverseModPrime2)%Modulus2;
auto recombinedValue=value1+Modulus1*crtCoefficient;
uncarriedLow[index]=(unsigned int)(recombinedValue&0xFFFFFFFF);
uncarriedHigh[index]=(unsigned int)(recombinedValue>>32);
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs
{
unsigned int* Buffer;
unsigned int* TemporaryBuffer;
const unsigned int* Twiddle;
int M;
int N1;
int N2;
unsigned int P;
unsigned long long Pm;
bool IsInverse;
bool ApplyTwiddleBefore;
bool ApplyTwiddleAfter;
int MaxBlocks;
int CurrentL;
}
;
#endif
inline __device__ void Device_NTT_Internal(Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs args){
auto buffer=args.Buffer;
auto temporaryBuffer=args.TemporaryBuffer;
auto transformLength=args.M;
auto length1=args.N1;
auto length2=args.N2;
auto isInverse=args.IsInverse;
if(transformLength<=512)
{
args.CurrentL=transformLength;
args.ApplyTwiddleBefore=false;
args.ApplyTwiddleAfter=false;
args.MaxBlocks=1;
Device_NTT_Shared_Row(args);
cg::this_grid().sync();
}
else
{
if(isInverse)
{
args.CurrentL=length2;
args.ApplyTwiddleBefore=false;
args.ApplyTwiddleAfter=true;
args.MaxBlocks=length1;
Device_NTT_Shared_Row(args);
cg::this_grid().sync();
Device_Transpose_32x32(buffer,temporaryBuffer,length2,length1);
cg::this_grid().sync();
args.Buffer=temporaryBuffer;
args.CurrentL=length1;
args.ApplyTwiddleBefore=false;
args.ApplyTwiddleAfter=false;
args.MaxBlocks=length2;
Device_NTT_Shared_Row(args);
args.Buffer=buffer;
cg::this_grid().sync();
Device_Transpose_32x32(temporaryBuffer,buffer,length1,length2);
cg::this_grid().sync();
}
else
{
Device_Transpose_32x32(buffer,temporaryBuffer,length2,length1);
cg::this_grid().sync();
args.Buffer=temporaryBuffer;
args.CurrentL=length1;
args.ApplyTwiddleBefore=false;
args.ApplyTwiddleAfter=true;
args.MaxBlocks=length2;
Device_NTT_Shared_Row(args);
args.Buffer=buffer;
cg::this_grid().sync();
Device_Transpose_32x32(temporaryBuffer,buffer,length1,length2);
cg::this_grid().sync();
args.CurrentL=length2;
args.ApplyTwiddleBefore=false;
args.ApplyTwiddleAfter=false;
args.MaxBlocks=length1;
Device_NTT_Shared_Row(args);
cg::this_grid().sync();
}
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs
{
unsigned int* Buffer;
unsigned int* TemporaryBuffer;
const unsigned int* Twiddle;
int M;
int N1;
int N2;
unsigned int P;
unsigned long long Pm;
bool IsInverse;
bool ApplyTwiddleBefore;
bool ApplyTwiddleAfter;
int MaxBlocks;
int CurrentL;
}
;
#endif
inline __device__ void Device_ForwardNTT(Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs args){
args.IsInverse=false;
Device_NTT_Internal(args);
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs
{
unsigned int* Buffer;
unsigned int* TemporaryBuffer;
const unsigned int* Twiddle;
int M;
int N1;
int N2;
unsigned int P;
unsigned long long Pm;
bool IsInverse;
bool ApplyTwiddleBefore;
bool ApplyTwiddleAfter;
int MaxBlocks;
int CurrentL;
}
;
#endif
inline __device__ void Device_InverseNTT(Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs args){
args.IsInverse=true;
Device_NTT_Internal(args);
}

inline __device__ void Device_ScalarAdd(unsigned int* left, unsigned int s, unsigned int* result, int numWords){
if (threadIdx.x == 0 && blockIdx.x == 0)
{
unsigned long long carry=s;
for(int j=0;j<numWords;j++)
{
auto sum=left[j]+carry;
result[j]=(unsigned int)sum;
carry=sum>>32;
}
}
}

inline __device__ void Device_ScalarSub(unsigned int* left, unsigned int s, unsigned int* result, int numWords){
if (threadIdx.x == 0 && blockIdx.x == 0)
{
long long borrow=s;
for(int j=0;j<numWords;j++)
{
auto diff=left[j]-borrow;
if(diff<0)
{
diff+=0x100000000L;
borrow=1;
}
else
{
borrow=0;
}
result[j]=(unsigned int)diff;
}
}
}

inline __device__ void Device_ScalarMul(unsigned int* left, unsigned int s, unsigned int* result, int numWords){
if (threadIdx.x == 0 && blockIdx.x == 0)
{
unsigned long long carry=0;
for(int j=0;j<numWords;j++)
{
auto prod=(unsigned long long)left[j]*s+carry;
result[j]=(unsigned int)(prod&0xFFFFFFFFul);
carry=prod>>32;
}
}
}

inline __device__ void Device_ChunkScalarMul(unsigned int* left, unsigned int s, unsigned int* result, long long* carryBuf, int numWords, int chunkSize){
auto totalChunks=(numWords+chunkSize-1)/chunkSize;
for (int chunkIdx = blockIdx.x * blockDim.x + threadIdx.x; chunkIdx < totalChunks; chunkIdx += gridDim.x * blockDim.x)
{
auto start=chunkIdx*chunkSize;
auto end=start+chunkSize;
if(end>numWords)
{
end=numWords;
}
unsigned long long carry=0;
for(int i=start;i<end;i++)
{
auto prod=(unsigned long long)left[i]*s+carry;
result[i]=(unsigned int)(prod&0xFFFFFFFFul);
carry=prod>>32;
}
carryBuf[chunkIdx+1]=(long long)carry;
}
}

#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
#define __GSHARP_STRUCT_Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs__
struct Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs
{
unsigned int* Buffer;
unsigned int* TemporaryBuffer;
const unsigned int* Twiddle;
int M;
int N1;
int N2;
unsigned int P;
unsigned long long Pm;
bool IsInverse;
bool ApplyTwiddleBefore;
bool ApplyTwiddleAfter;
int MaxBlocks;
int CurrentL;
}
;
#endif
extern "C" __global__ void Kernel_MultiplyNtt_SharedRow(unsigned int* __restrict__ buffer, unsigned int* __restrict__ temporaryBuffer, unsigned int* __restrict__ twiddle, int m, int n1, int n2, unsigned int p, unsigned long long pm, bool applyTwiddleBefore, bool applyTwiddleAfter, int maxBlocks, int currentL){
auto args=([&]() {
Kassyi_GSharp_Stdlib_Math_Contexts_NumberTheoreticTransformKernelArgs _tmp{}; _tmp.Buffer = buffer; _tmp.TemporaryBuffer = temporaryBuffer; _tmp.Twiddle = twiddle; _tmp.M = m; _tmp.N1 = n1; _tmp.N2 = n2; _tmp.P = p; _tmp.Pm = pm; _tmp.ApplyTwiddleBefore = applyTwiddleBefore; _tmp.ApplyTwiddleAfter = applyTwiddleAfter; _tmp.MaxBlocks = maxBlocks; _tmp.CurrentL = currentL; return _tmp; }
());
Device_NTT_Shared_Row(args);
}
