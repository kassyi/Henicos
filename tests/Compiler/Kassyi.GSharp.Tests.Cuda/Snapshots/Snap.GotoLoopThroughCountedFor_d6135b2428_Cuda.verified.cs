extern "C" __global__ void __gsharp_k_TestNs__SnapshotKernels__GotoThroughForKernel(GSharpArray<unsigned int> __gsharp_id_0_result, unsigned int __gsharp_epoch){
__gsharp_launch_epoch = __gsharp_epoch;
unsigned long long __gsharp_iteration_0 = 0ull;
int __gsharp_id_1_t = (int)threadIdx.x;
int __gsharp_id_2_p = (-1);
__gsharp_iteration_0=0ull;
__gsharp_id_3_L1:
++__gsharp_iteration_0;
__gsharp_id_2_p=__gsharp_add_i32(__gsharp_id_2_p,1);
__gsharp_array_at(__gsharp_id_0_result, __gsharp_add_i32(__gsharp_id_1_t,__gsharp_mul_i32(32,__gsharp_id_2_p)), "G#: a null array is read at an unknown place")=__match_any_sync(__activemask(),__gsharp_iteration_0);
for(int __gsharp_id_4_i = 0;__gsharp_id_4_i<2;__gsharp_id_4_i++)
{
if(__gsharp_id_2_p<1&&(__gsharp_id_1_t&3)==__gsharp_add_i32(__gsharp_id_4_i,1))
{
goto __gsharp_id_3_L1;
}
__gsharp_array_at(__gsharp_id_0_result, __gsharp_add_i32(__gsharp_id_1_t,__gsharp_mul_i32(32,__gsharp_add_i32(__gsharp_add_i32(2,__gsharp_mul_i32(2,__gsharp_id_2_p)),__gsharp_id_4_i))), "G#: a null array is read at an unknown place")=__match_any_sync(__match_any_sync(__activemask(),__gsharp_iteration_0),(unsigned int)__gsharp_id_4_i);
}
__gsharp_array_at(__gsharp_id_0_result, __gsharp_add_i32(__gsharp_id_1_t,192), "G#: a null array is read at an unknown place")=__activemask();
}
