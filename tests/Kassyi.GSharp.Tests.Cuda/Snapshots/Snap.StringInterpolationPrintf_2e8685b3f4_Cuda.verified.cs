extern "C" __global__ void StringsAndAttributesTarget_StringInterpolationPrintf(GSharpArray<unsigned int> data){
unsigned int reg = data[0];
mov.u32 reg, 42;
;
data[0]=reg;
}
