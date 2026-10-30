extern "C" __declspec(dllexport) void __gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__MicroModifiersTarget__ForeachTupleDeconstruction(GSharpArray<System_ValueTuple<int, int>> __gsharp_id_0_pairs, GSharpArray<int> __gsharp_id_1_output){
int __gsharp_id_2_sum = 0;
/*unsupported:ForEachVariableStatement*/
__gsharp_id_1_output[0]=__gsharp_id_2_sum;
}
extern "C" __declspec(dllexport) void __gsharp_launch_Kassyi__GSharp__Tests__Shared__Syntax__MicroModifiersTarget__ForeachTupleDeconstruction(uint32_t __gsharp_grid_dim_x, uint32_t __gsharp_grid_dim_y, uint32_t __gsharp_grid_dim_z, uint32_t __gsharp_block_dim_x, uint32_t __gsharp_block_dim_y, uint32_t __gsharp_block_dim_z, uint32_t __gsharp_shared_mem_bytes, GSharpArray<System_ValueTuple<int, int>> __gsharp_id_0_pairs, GSharpArray<int> __gsharp_id_1_output)
{
__gsharp_host_last_error.Reset();
GSharpErrorSlot __gsharp_launch_error;
int32_t __gsharp_total_blocks = (int32_t)(__gsharp_grid_dim_x * __gsharp_grid_dim_y * __gsharp_grid_dim_z);
#pragma omp parallel for schedule(dynamic)
for (int32_t __gsharp_block_index = 0; __gsharp_block_index < __gsharp_total_blocks; __gsharp_block_index++)
{
if (__gsharp_launch_error.HasError()) continue;
try
{
int32_t __gsharp_bx = __gsharp_block_index % __gsharp_grid_dim_x;
int32_t __gsharp_by = (__gsharp_block_index / __gsharp_grid_dim_x) % __gsharp_grid_dim_y;
int32_t __gsharp_bz = __gsharp_block_index / (__gsharp_grid_dim_x * __gsharp_grid_dim_y);
__gsharp_reset_smem(__gsharp_shared_mem_bytes);
int32_t __gsharp_threads_per_block = (int32_t)(__gsharp_block_dim_x * __gsharp_block_dim_y * __gsharp_block_dim_z);
for (int32_t __gsharp_thread_index = 0; __gsharp_thread_index < __gsharp_threads_per_block; __gsharp_thread_index++)
{
if (__gsharp_launch_error.HasError()) continue;
__gsharp_active_error_slot = &__gsharp_launch_error;
int32_t __gsharp_tx = __gsharp_thread_index % __gsharp_block_dim_x;
int32_t __gsharp_ty = (__gsharp_thread_index / __gsharp_block_dim_x) % __gsharp_block_dim_y;
int32_t __gsharp_tz = __gsharp_thread_index / (__gsharp_block_dim_x * __gsharp_block_dim_y);
__gsharp_set_thread_idx(__gsharp_tx, __gsharp_ty, __gsharp_tz, __gsharp_bx, __gsharp_by, __gsharp_bz, __gsharp_block_dim_x, __gsharp_block_dim_y, __gsharp_block_dim_z, __gsharp_grid_dim_x, __gsharp_grid_dim_y, __gsharp_grid_dim_z);
__gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__MicroModifiersTarget__ForeachTupleDeconstruction(__gsharp_id_0_pairs, __gsharp_id_1_output);
}
}
catch (const __gsharp_step_abort&) {}
catch (const std::bad_alloc&)
{
__gsharp_launch_error.Report(GSharpErrorCode::InvalidOperation, "G#: the native CPU path could not allocate memory for a kernel step");
}
}
if (__gsharp_launch_error.HasError()) {
__gsharp_host_last_error.Report(static_cast<GSharpErrorCode>(__gsharp_launch_error.code.load(std::memory_order_acquire)), __gsharp_launch_error.message);
}
}
