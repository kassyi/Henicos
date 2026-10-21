inline __device__ void SwitchGotoCase(GSharpArray<int> __gsharp_id_0_output, int __gsharp_id_1_mode){
int __gsharp_id_2_acc = 0;
switch(__gsharp_id_1_mode)
{
case 1:
__gsharp_switch_1_state_1:
{
__gsharp_id_2_acc+=10;
goto __gsharp_switch_1_state_2;
}
case 2:
__gsharp_switch_1_state_2:
{
__gsharp_id_2_acc+=20;
goto __gsharp_switch_1_state_default;
}
default:
__gsharp_switch_1_state_default:
{
__gsharp_id_2_acc+=5;
break;
}
}
__gsharp_id_0_output[0]=__gsharp_id_2_acc;
}
