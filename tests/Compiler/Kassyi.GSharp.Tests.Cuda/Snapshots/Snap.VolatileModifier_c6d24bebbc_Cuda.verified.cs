#ifndef __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod__
#define __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod__
struct __gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod
{
volatile int __gsharp_mb_Flag;
}
;
static_assert(sizeof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod)==4,"Kassyi.GSharp.Tests.Shared.Syntax.VolatileState_MicroMod: size");
static_assert(alignof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod)==4,"Kassyi.GSharp.Tests.Shared.Syntax.VolatileState_MicroMod: alignment");
static_assert(offsetof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod,__gsharp_mb_Flag)==0,"Kassyi.GSharp.Tests.Shared.Syntax.VolatileState_MicroMod.Flag: offset");
#endif
extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__MicroModifiersTarget__VolatileModifier(GSharpArray<__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod> __gsharp_id_0_states){
__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod __gsharp_id_1_s = __gsharp_array_ref(__gsharp_id_0_states, "G#: a null array is read at MicroModifiersTests.cs(36,36)")[0];
__gsharp_id_1_s.__gsharp_mb_Flag=1;
__gsharp_array_ref(__gsharp_id_0_states, "G#: a null array is read at MicroModifiersTests.cs(38,9)")[0]=__gsharp_id_1_s;
}
