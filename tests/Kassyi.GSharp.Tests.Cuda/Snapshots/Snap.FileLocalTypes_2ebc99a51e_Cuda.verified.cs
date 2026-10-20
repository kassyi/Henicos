#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod__
#define __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod__
struct Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod
{
int __gsharp_mb_Left;
int __gsharp_mb_Right;
}
;
#endif
extern "C" __global__ void MicroModifiersTarget_FileLocalTypes(GSharpArray<int> __gsharp_id_0_output){
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod __gsharp_id_1_p = ([&]() {
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod __gsharp_tmp{}; __gsharp_tmp.__gsharp_mb_Left = 10; __gsharp_tmp.__gsharp_mb_Right = 20; return __gsharp_tmp; }
());
__gsharp_id_0_output[0]=__gsharp_id_1_p.__gsharp_mb_Left+__gsharp_id_1_p.__gsharp_mb_Right;
}
