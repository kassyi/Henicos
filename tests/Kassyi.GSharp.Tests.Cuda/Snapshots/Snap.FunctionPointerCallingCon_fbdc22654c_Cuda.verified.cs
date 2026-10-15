extern "C" __global__ void MicroModifiersTarget_FunctionPointerCallingConventions(void* fn, GSharpArray<int> output){
output[0]=fn(10);
}

// GSH0003