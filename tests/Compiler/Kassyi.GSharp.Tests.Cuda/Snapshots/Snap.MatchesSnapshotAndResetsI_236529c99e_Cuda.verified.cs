#ifndef __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData__
#define __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData__
struct __gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData
{
int __gsharp_mb_X;
float __gsharp_mb_Y;
}
;
static_assert(sizeof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData)==8,"Kassyi.GSharp.Tests.Shared.Syntax.SampleData: size");
static_assert(alignof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData)==4,"Kassyi.GSharp.Tests.Shared.Syntax.SampleData: alignment");
static_assert(offsetof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData,__gsharp_mb_X)==0,"Kassyi.GSharp.Tests.Shared.Syntax.SampleData.X: offset");
static_assert(offsetof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData,__gsharp_mb_Y)==4,"Kassyi.GSharp.Tests.Shared.Syntax.SampleData.Y: offset");
#endif
inline __device__ void DefaultLiteral(GSharpArray<__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData> __gsharp_id_0_items, GSharpArray<int> __gsharp_id_1_counts){
__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__SampleData __gsharp_id_2_s = {};
int __gsharp_id_3_c = {};
__gsharp_array_at(__gsharp_id_0_items, 0, "G#: a null array is read at ControlFlowTests.cs(195,9)")=__gsharp_id_2_s;
__gsharp_array_at(__gsharp_id_1_counts, 0, "G#: a null array is read at ControlFlowTests.cs(196,9)")=__gsharp_id_3_c;
}
