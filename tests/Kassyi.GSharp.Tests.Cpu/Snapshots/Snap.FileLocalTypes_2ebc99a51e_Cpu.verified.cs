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
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod __gsharp_tmp{}; __gsharp_tmp.Left = 10; __gsharp_tmp.Right = 20; return __gsharp_tmp; }
());
output[0]=p.Left+p.Right;
}
extern "C" __declspec(dllexport) void __gsharp_launch_MicroModifiersTarget_FileLocalTypes(uint32_t gridDimX, uint32_t gridDimY, uint32_t gridDimZ, uint32_t blockDimX, uint32_t blockDimY, uint32_t blockDimZ, GSharpArray<int> output)
{
__gsharp_host_last_error.Reset();
GSharpErrorSlot __gsharp_launch_error;
int32_t __gsharp_total_blocks = (int32_t)(gridDimX * gridDimY * gridDimZ);
#pragma omp parallel for schedule(dynamic)
for (int32_t __gsharp_block_index = 0; __gsharp_block_index < __gsharp_total_blocks; __gsharp_block_index++)
{
if (__gsharp_launch_error.HasError()) continue;
int32_t bx = __gsharp_block_index % gridDimX;
int32_t by = (__gsharp_block_index / gridDimX) % gridDimY;
int32_t bz = __gsharp_block_index / (gridDimX * gridDimY);
__gsharp_reset_smem();
int32_t __gsharp_threads_per_block = (int32_t)(blockDimX * blockDimY * blockDimZ);
#pragma omp parallel for schedule(static)
for (int32_t __gsharp_thread_index = 0; __gsharp_thread_index < __gsharp_threads_per_block; __gsharp_thread_index++)
{
if (__gsharp_launch_error.HasError()) continue;
__gsharp_active_error_slot = &__gsharp_launch_error;
int32_t tx = __gsharp_thread_index % blockDimX;
int32_t ty = (__gsharp_thread_index / blockDimX) % blockDimY;
int32_t tz = __gsharp_thread_index / (blockDimX * blockDimY);
__gsharp_set_thread_idx(tx, ty, tz, bx, by, bz, blockDimX, blockDimY, blockDimZ, gridDimX, gridDimY, gridDimZ);
MicroModifiersTarget_FileLocalTypes(output);
}
}
if (__gsharp_launch_error.HasError()) {
__gsharp_host_last_error.Report(static_cast<GSharpErrorCode>(__gsharp_launch_error.code.load(std::memory_order_acquire)), __gsharp_launch_error.message);
}
}
