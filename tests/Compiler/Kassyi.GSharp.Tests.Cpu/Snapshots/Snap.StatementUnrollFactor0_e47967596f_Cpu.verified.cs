extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__TestCases__Analyzers__SYN_u156_uLoopUnrollStatements__StatementUnrollFactor0(GSharpArray<int> __gsharp_id_0_data, unsigned int __gsharp_epoch){
__gsharp_launch_epoch = __gsharp_epoch;
int __gsharp_id_1_sum = 0;
#pragma unroll
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<4;__gsharp_id_2_i++)
{
__gsharp_id_1_sum=__gsharp_add_i32(__gsharp_id_1_sum,__gsharp_id_2_i);
}
__gsharp_array_at(__gsharp_id_0_data, 0, "G#: a null array is read at LoopUnrollStatements.cs(78,9)")=__gsharp_id_1_sum;
}
