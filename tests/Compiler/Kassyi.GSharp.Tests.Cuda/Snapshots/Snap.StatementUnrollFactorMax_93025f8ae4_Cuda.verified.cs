extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__TestCases__Analyzers__SYN_u156_uLoopUnrollStatements__StatementUnrollFactorMax(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_sum = 0;
#pragma unroll 2147483647
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<4;__gsharp_id_2_i++)
{
__gsharp_id_1_sum=__gsharp_add_i32(__gsharp_id_1_sum,__gsharp_id_2_i);
}
__gsharp_id_0_data[0]=__gsharp_id_1_sum;
}
