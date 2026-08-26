// GSharp Prelude
#include <cooperative_groups.h>
namespace cg=cooperative_groups;

__device__ __forceinline__ unsigned int __gsharp_add_cc(unsigned int a,unsigned int b,unsigned long long*carry){
unsigned int res;
unsigned int c_in=(unsigned int)*carry;
unsigned int c_out;
asm volatile(
"add.cc.u32 %0, %2, -1;\n\t"
"addc.cc.u32 %0, %3, %4;\n\t"
"addc.u32 %1, 0, 0;"
:"=&r"(res),"=r"(c_out)
:"r"(c_in),"r"(a),"r"(b)
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
"sub.cc.u32 %1, 0, %3;\n\t"
"subc.cc.u32 %0, %4, %5;\n\t"
"subc.u32 %2, 0, 0;"
:"=&r"(res),"=&r"(dummy),"=r"(b_out)
:"r"(b_in),"r"(a),"r"(b)
);
*borrow=b_out&1;
return res;
}

__device__ __forceinline__ unsigned long long __gsharp_mul_wide(unsigned int a, unsigned int b) {
    unsigned long long res;
    asm volatile("mul.wide.u32 %0, %1, %2;" : "=l"(res) : "r"(a), "r"(b));
    return res;
}

__device__ __forceinline__ unsigned int __gsharp_rotl32(unsigned int value, int offset) {
    offset &= 31;
    return __funnelshift_l(value, value, offset);
}
__device__ __forceinline__ unsigned int __gsharp_rotr32(unsigned int value, int offset) {
    offset &= 31;
    return __funnelshift_r(value, value, offset);
}
__device__ __forceinline__ unsigned long long __gsharp_rotl64(unsigned long long value, int offset) {
    offset &= 63;
    return offset ? (value << offset) | (value >> (64 - offset)) : value;
}
__device__ __forceinline__ unsigned long long __gsharp_rotr64(unsigned long long value, int offset) {
    offset &= 63;
    return offset ? (value >> offset) | (value << (64 - offset)) : value;
}
__device__ __forceinline__ int __gsharp_tzc_32(unsigned int value) {
    return value ? __ffs(value) - 1 : 32;
}
__device__ __forceinline__ int __gsharp_tzc_64(unsigned long long value) {
    return value ? __ffsll(value) - 1 : 64;
}
__device__ __forceinline__ int __gsharp_log2_32(unsigned int value) {
    return value ? 31 - __clz(value) : 0;
}
__device__ __forceinline__ int __gsharp_log2_64(unsigned long long value) {
    return value ? 63 - __clzll(value) : 0;
}
__device__ __forceinline__ int __gsharp_lzc_32(unsigned int value) {
    return value ? __clz(value) : 32;
}
__device__ __forceinline__ int __gsharp_lzc_64(unsigned long long value) {
    return value ? __clzll(value) : 64;
}
