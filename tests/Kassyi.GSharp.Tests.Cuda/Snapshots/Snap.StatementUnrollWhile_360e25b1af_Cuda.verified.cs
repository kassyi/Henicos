extern "C" __global__ void SYN_156_LoopUnrollStatements_StatementUnrollWhile(GSharpArray<int> __gsharp_id_0_data){
int __gsharp_id_1_count = 0;
int __gsharp_id_2_sum = 0;
#pragma unroll
while(__gsharp_id_1_count<3)
{
__gsharp_id_2_sum+=5;
__gsharp_id_1_count++;
}
__gsharp_id_0_data[0]=__gsharp_id_2_sum;
}
