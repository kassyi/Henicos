#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod__
#define __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod__
struct Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod
{
int Left;
int Right;
}
;
#endif
extern "C" __declspec(dllexport) void MicroModifiersTarget_FileLocalTypes(GSharpArray<int> output){
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod p = ([&]() {
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod _tmp{}; _tmp.Left = 10; _tmp.Right = 20; return _tmp; }
());
output[0]=p.Left+p.Right;
}
extern "C" __declspec(dllexport) void __gsharp_launch_MicroModifiersTarget_FileLocalTypes(uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ, GSharpArray<int> output)
{
__gsharp_host_last_error.Reset();
GSharpErrorSlot _gsharp_launch_error;
int32_t _gsharp_total_blocks = (int32_t)(gridDimX * gridDimY * gridDimZ);
#pragma omp parallel for schedule(dynamic)
for (int32_t _b_idx = 0; _b_idx < _gsharp_total_blocks; _b_idx++)
{
if (_gsharp_launch_error.HasError()) continue;
int32_t bx = _b_idx % gridDimX;
int32_t by = (_b_idx / gridDimX) % gridDimY;
int32_t bz = _b_idx / (gridDimX * gridDimY);
__gsharp_reset_smem();
int32_t _gsharp_total_threads = (int32_t)(blockDimX * blockDimY * blockDimZ);
#pragma omp parallel for schedule(static)
for (int32_t _t_idx = 0; _t_idx < _gsharp_total_threads; _t_idx++)
{
if (_gsharp_launch_error.HasError()) continue;
__gsharp_active_error_slot = &_gsharp_launch_error;
int32_t tx = _t_idx % blockDimX;
int32_t ty = (_t_idx / blockDimX) % blockDimY;
int32_t tz = _t_idx / (blockDimX * blockDimY);
__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);
MicroModifiersTarget_FileLocalTypes(output);
}
}
if (_gsharp_launch_error.HasError()) {
__gsharp_host_last_error.Report(static_cast<GSharpErrorCode>(_gsharp_launch_error.code.load(std::memory_order_acquire)), _gsharp_launch_error.message);
}
}
