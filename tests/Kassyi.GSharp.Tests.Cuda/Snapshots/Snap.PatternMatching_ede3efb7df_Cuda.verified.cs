extern "C" __global__ void ControlFlowKernels_SwitchExpressionKernel(GSharpArray<int> input, GSharpArray<int> output){
int tid = threadIdx.x;
int val = input[tid];
output[tid]=([&]() {
switch(val)
{
case 1: return 10;
case 2: return 20;
case 3: return 30;
default: return 99;
}
}());
}
