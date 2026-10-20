extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollFactor0(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_sum = 0;
#pragma unroll
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<4;__gsharp_id_2_i++)
{
__gsharp_id_1_sum+=__gsharp_id_2_i;
}
__gsharp_id_0_data[0]=__gsharp_id_1_sum;
}
