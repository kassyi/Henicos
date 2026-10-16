extern "C" __global__ void StringsAndAttributesTarget_Utf8StringLiteral(GSharpArray<unsigned char> output){
GSharpArray<unsigned char> utf8 = "GPU"u8;
output[0]=utf8[0];
}
