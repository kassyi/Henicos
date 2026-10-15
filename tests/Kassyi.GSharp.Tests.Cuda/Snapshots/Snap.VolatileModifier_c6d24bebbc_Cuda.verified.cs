#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod__
#define __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod__
struct Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod
{
volatile int Flag;
}
;
#endif
extern "C" __global__ void MicroModifiersTarget_VolatileModifier(GSharpArray<Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod> states){
Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod s = states[0];
s.Flag=1;
states[0]=s;
}
