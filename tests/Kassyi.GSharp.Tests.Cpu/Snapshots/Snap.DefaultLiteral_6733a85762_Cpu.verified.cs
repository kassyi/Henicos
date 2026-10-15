#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_SampleData__
#define __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_SampleData__
struct Kassyi_GSharp_Tests_Shared_Syntax_SampleData
{
int X;
float Y;
}
;
#endif
inline void DefaultLiteral(GSharpArray<Kassyi_GSharp_Tests_Shared_Syntax_SampleData> items, GSharpArray<int> counts){
Kassyi_GSharp_Tests_Shared_Syntax_SampleData s = {};
int c = {};
items[0]=s;
counts[0]=c;
}
