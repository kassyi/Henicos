// Input of PreludeSpillBudgetTests: the test puts #include "<Prelude.cu>" in front of it. The recursive device
// function makes ptxas compile every function that is not inlined with the call ABI, as in a G# module with recursion.
__device__ int __gsharp_fn_Depth(int n, int* a) {
if (n < 0) { __gsharp_trap(GSharpErrorCode::IndexOutOfRange, 1u); }
return n == 0 ? a[0] : __gsharp_fn_Depth(n - 1, a) + 1;
}
extern "C" __global__ void __gsharp_k_Probe(int* a, unsigned int __gsharp_epoch) {
__gsharp_launch_epoch = __gsharp_epoch;
a[threadIdx.x] = __gsharp_fn_Depth(a[threadIdx.x], a);
}
