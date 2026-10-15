extern "C" __global__ void StringsAndAttributesTarget_LoopUnrollAttribute(GSharpArray<int> output){
int acc = 0;
#pragma unroll 4
for(int i = 0;i<4;i++)
{
acc+=i;
}
output[0]=acc;
}
