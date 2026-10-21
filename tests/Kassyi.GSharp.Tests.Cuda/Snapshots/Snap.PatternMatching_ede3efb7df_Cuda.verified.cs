extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel(GSharpArray<int> __gsharp_id_0_input, GSharpArray<int> __gsharp_id_1_output){
int __gsharp_id_2_tid = threadIdx.x;
int __gsharp_id_3_val = __gsharp_id_0_input[__gsharp_id_2_tid];
__gsharp_id_1_output[__gsharp_id_2_tid]=([&]() {
switch(__gsharp_id_3_val)
{
case 1: return 10;
case 2: return 20;
case 3: return 30;
default: return 99;
}
}());
}
