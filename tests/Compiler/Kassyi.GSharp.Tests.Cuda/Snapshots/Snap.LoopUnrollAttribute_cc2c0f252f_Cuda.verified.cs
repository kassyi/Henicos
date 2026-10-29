extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__StringsAndAttributesTarget__LoopUnrollAttribute(GSharpArray<int> __gsharp_id_0_output){
int __gsharp_id_1_acc = 0;
#pragma unroll 4
for(int __gsharp_id_2_i = 0;__gsharp_id_2_i<4;__gsharp_id_2_i++)
{
__gsharp_id_1_acc+=__gsharp_id_2_i;
}
__gsharp_array_at(__gsharp_id_0_output, 0, "G#: a null array is read at StringsAndAttributesTests.cs(40,9)")=__gsharp_id_1_acc;
}
