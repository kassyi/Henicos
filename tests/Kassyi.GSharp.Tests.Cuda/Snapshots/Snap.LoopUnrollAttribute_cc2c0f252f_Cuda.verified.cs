extern "C" __global__ void StringsAndAttributesTarget_LoopUnrollAttribute(GSharpArray<int> __gsharp_id_0_output){
int __gsharp_id_1_acc = 0;
#pragma unroll 4
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<4;__gsharp_id_2_i++)
{
__gsharp_id_1_acc+=__gsharp_id_2_i;
}
__gsharp_id_0_output[0]=__gsharp_id_1_acc;
}
