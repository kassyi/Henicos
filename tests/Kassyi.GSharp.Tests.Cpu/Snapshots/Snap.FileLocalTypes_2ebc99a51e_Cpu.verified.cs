#ifndef __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod__
#define __GSHARP_STRUCT_Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod__
struct Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod
{
int __gsharp_mb_Left;
int __gsharp_mb_Right;
}
;
#endif
extern "C" __declspec(dllexport) void MicroModifiersTarget_FileLocalTypes(GSharpArray<int> __gsharp_id_0_output){
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod __gsharp_id_1_p = ([&]() {
Kassyi_GSharp_Tests_Shared_Syntax_FileLocalPair_MicroMod __gsharp_tmp{}; __gsharp_tmp.__gsharp_mb_Left = 10; __gsharp_tmp.__gsharp_mb_Right = 20; return __gsharp_tmp; }
());
__gsharp_id_0_output[0]=__gsharp_id_1_p.__gsharp_mb_Left+__gsharp_id_1_p.__gsharp_mb_Right;
}
extern "C" __declspec(dllexport) void __gsharp_launch_MicroModifiersTarget_FileLocalTypes(uint32_t __gsharp_grid_dim_x, uint32_t __gsharp_grid_dim_y, uint32_t __gsharp_grid_dim_z, uint32_t __gsharp_block_dim_x, uint32_t __gsharp_block_dim_y, uint32_t __gsharp_block_dim_z, GSharpArray<int> __gsharp_id_0_output)
{
__gsharp_host_last_error.Reset();
GSharpErrorSlot __gsharp_launch_error;
int32_t __gsharp_total_blocks = (int32_t)(__gsharp_grid_dim_x * __gsharp_grid_dim_y * __gsharp_grid_dim_z);
#pragma omp parallel for schedule(dynamic)
for (int32_t __gsharp_block_index = 0; __gsharp_block_index < __gsharp_total_blocks; __gsharp_block_index++)
{
if (__gsharp_launch_error.HasError()) continue;
int32_t __gsharp_bx = __gsharp_block_index % __gsharp_grid_dim_x;
int32_t __gsharp_by = (__gsharp_block_index / __gsharp_grid_dim_x) % __gsharp_grid_dim_y;
int32_t __gsharp_bz = __gsharp_block_index / (__gsharp_grid_dim_x * __gsharp_grid_dim_y);
__gsharp_reset_smem();
int32_t __gsharp_threads_per_block = (int32_t)(__gsharp_block_dim_x * __gsharp_block_dim_y * __gsharp_block_dim_z);
#pragma omp parallel for schedule(static)
for (int32_t __gsharp_thread_index = 0; __gsharp_thread_index < __gsharp_threads_per_block; __gsharp_thread_index++)
{
if (__gsharp_launch_error.HasError()) continue;
__gsharp_active_error_slot = &__gsharp_launch_error;
int32_t __gsharp_tx = __gsharp_thread_index % __gsharp_block_dim_x;
int32_t __gsharp_ty = (__gsharp_thread_index / __gsharp_block_dim_x) % __gsharp_block_dim_y;
int32_t __gsharp_tz = __gsharp_thread_index / (__gsharp_block_dim_x * __gsharp_block_dim_y);
__gsharp_set_thread_idx(__gsharp_tx, __gsharp_ty, __gsharp_tz, __gsharp_bx, __gsharp_by, __gsharp_bz, __gsharp_block_dim_x, __gsharp_block_dim_y, __gsharp_block_dim_z, __gsharp_grid_dim_x, __gsharp_grid_dim_y, __gsharp_grid_dim_z);
MicroModifiersTarget_FileLocalTypes(__gsharp_id_0_output);
}
}
if (__gsharp_launch_error.HasError()) {
__gsharp_host_last_error.Report(static_cast<GSharpErrorCode>(__gsharp_launch_error.code.load(std::memory_order_acquire)), __gsharp_launch_error.message);
}
}
