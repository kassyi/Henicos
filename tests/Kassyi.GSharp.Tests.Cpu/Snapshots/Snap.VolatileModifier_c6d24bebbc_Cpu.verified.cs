#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod__
#define __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod__
struct Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod
{
volatile int Flag;
}
;
#endif
extern "C" __declspec(dllexport) void MicroModifiersTarget_VolatileModifier(GSharpArray<Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod> states){
Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod s = states[0];
s.Flag=1;
states[0]=s;
}
extern "C" __declspec(dllexport) void __gsharp_launch_MicroModifiersTarget_VolatileModifier(uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ, GSharpArray<Kassyi_GSharp_Tests_Shared_Syntax_VolatileState_MicroMod> states)
{
int32_t _gsharp_total_blocks = (int32_t)(gridDimX * gridDimY * gridDimZ);
#pragma omp parallel for schedule(dynamic)
for (int32_t _b_idx = 0; _b_idx < _gsharp_total_blocks; _b_idx++)
{
int32_t bx = _b_idx % gridDimX;
int32_t by = (_b_idx / gridDimX) % gridDimY;
int32_t bz = _b_idx / (gridDimX * gridDimY);
__gsharp_reset_smem();
int32_t _gsharp_total_threads = (int32_t)(blockDimX * blockDimY * blockDimZ);
#pragma omp parallel for schedule(static)
for (int32_t _t_idx = 0; _t_idx < _gsharp_total_threads; _t_idx++)
{
int32_t tx = _t_idx % blockDimX;
int32_t ty = (_t_idx / blockDimX) % blockDimY;
int32_t tz = _t_idx / (blockDimX * blockDimY);
__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);
MicroModifiersTarget_VolatileModifier(states);
}
}
}
