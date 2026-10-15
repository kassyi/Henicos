#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod__
#define __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod__
struct Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod
{
int Left;
int Right;
}
;
#endif
extern "C" __global__ void MicroModifiersTarget_FileLocalTypes(GSharpArray<int> output){
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod p = ([&]() {
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod _tmp{}; _tmp.Left = 10; _tmp.Right = 20; return _tmp; }
());
output[0]=p.Left+p.Right;
}
