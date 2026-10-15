#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_Pair_StringsTest_System_Int32__
#define __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_Pair_StringsTest_System_Int32__
template <typename T>
struct Kassyi_GSharp_Tests_Shared_Syntax_Pair_StringsTest
{
int First;
int Second;
}
;
#endif
extern "C" __global__ void StringsAndAttributesTarget_GenericStruct(GSharpArray<Kassyi_GSharp_Tests_Shared_Syntax_Pair_StringsTest<int>> items, GSharpArray<int> output){
Kassyi_GSharp_Tests_Shared_Syntax_Pair_StringsTest<int> p = items[0];
output[0]=p.First+p.Second;
}
