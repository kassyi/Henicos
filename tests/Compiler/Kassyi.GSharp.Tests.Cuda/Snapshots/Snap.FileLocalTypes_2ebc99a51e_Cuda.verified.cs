#ifndef __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0__
#define __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0__
struct __gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0
{
int __gsharp_mb_Left;
int __gsharp_mb_Right;
}
;
static_assert(sizeof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0)==8,"Kassyi.GSharp.Tests.Shared.Syntax.FileLocalPair_MicroMod(file 0): size");
static_assert(alignof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0)==4,"Kassyi.GSharp.Tests.Shared.Syntax.FileLocalPair_MicroMod(file 0): alignment");
static_assert(offsetof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0,__gsharp_mb_Left)==0,"Kassyi.GSharp.Tests.Shared.Syntax.FileLocalPair_MicroMod(file 0).Left: offset");
static_assert(offsetof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0,__gsharp_mb_Right)==4,"Kassyi.GSharp.Tests.Shared.Syntax.FileLocalPair_MicroMod(file 0).Right: offset");
#endif
extern "C" __global__ void __gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__MicroModifiersTarget__FileLocalTypes(GSharpArray<int> __gsharp_id_0_output){
__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0 __gsharp_id_1_p = ([&]() {
__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__FileLocalPair_uMicroMod_f0 __gsharp_tmp{}; __gsharp_tmp.__gsharp_mb_Left = 10; __gsharp_tmp.__gsharp_mb_Right = 20; return __gsharp_tmp; }
());
__gsharp_id_0_output[0]=__gsharp_add_i32(__gsharp_id_1_p.__gsharp_mb_Left,__gsharp_id_1_p.__gsharp_mb_Right);
}
