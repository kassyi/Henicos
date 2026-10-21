extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__TestCases__Analyzers__SYN_u156_uLoopUnrollStatements__StatementUnrollNested(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_sum = 0;
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<2;__gsharp_id_2_i++)
{
#pragma unroll
for(int __gsharp_id_3_j = 0;__gsharp_id_3_j<3;__gsharp_id_3_j++)
{
__gsharp_id_1_sum+=(__gsharp_id_2_i+1)*(__gsharp_id_3_j+1);
}
}
__gsharp_id_0_data[0]=__gsharp_id_1_sum;
}
