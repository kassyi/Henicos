extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollSingleStatement(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_sum = 0;
#pragma unroll
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<4;__gsharp_id_2_i++)
{
__gsharp_id_1_sum+=2;
}
__gsharp_id_0_data[0]=__gsharp_id_1_sum;
}
