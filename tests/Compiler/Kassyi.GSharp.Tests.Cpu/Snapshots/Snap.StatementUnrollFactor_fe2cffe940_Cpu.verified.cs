extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__TestCases__Analyzers__SYN_u156_uLoopUnrollStatements__StatementUnrollFactor(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_sum = 0;
#pragma unroll 4
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<4;__gsharp_id_2_i++)
{
__gsharp_id_1_sum+=(__gsharp_id_2_i+1)*10;
}
__gsharp_id_0_data[0]=__gsharp_id_1_sum;
}
