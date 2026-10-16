inline void SwitchGotoCase(GSharpArray<int> output, int mode){
int acc = 0;
switch(mode)
{
case 1:
__gsharp_switch_1_state_1:
{
acc+=10;
goto __gsharp_switch_1_state_2;
}
case 2:
__gsharp_switch_1_state_2:
{
acc+=20;
goto __gsharp_switch_1_state_default;
}
default:
__gsharp_switch_1_state_default:
{
acc+=5;
break;
}
}
output[0]=acc;
}
