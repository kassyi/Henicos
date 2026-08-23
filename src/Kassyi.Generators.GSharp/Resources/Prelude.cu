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
