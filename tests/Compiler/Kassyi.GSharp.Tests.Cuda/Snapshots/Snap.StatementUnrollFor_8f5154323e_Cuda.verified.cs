extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__TestCases__Analyzers__SYN_u156_uLoopUnrollStatements__StatementUnrollFor(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_sum = 0;
#pragma unroll
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<4;__gsharp_id_2_i++)
{
__gsharp_id_1_sum=__gsharp_add_i32(__gsharp_id_1_sum,__gsharp_add_i32(__gsharp_id_2_i,1));
}
__gsharp_id_0_data[0]=__gsharp_id_1_sum;
}
