extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel(GSharpArray<int> __gsharp_id_0_input, GSharpArray<int> __gsharp_id_1_output, unsigned int __gsharp_epoch){
__gsharp_launch_epoch = __gsharp_epoch;
int __gsharp_id_2_tid = (int)threadIdx.x;
int __gsharp_id_3_val = __gsharp_array_at(__gsharp_id_0_input, __gsharp_id_2_tid, "G#: a null array is read at ControlFlowKernels.cu.cs(36,19)");
__gsharp_array_at(__gsharp_id_1_output, __gsharp_id_2_tid, "G#: a null array is read at ControlFlowKernels.cu.cs(37,9)")=([&]() {
switch(__gsharp_id_3_val)
{
case 1: return 10;
case 2: return 20;
case 3: return 30;
default: return 99;
}
}());
}
