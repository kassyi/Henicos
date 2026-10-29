struct __gsharp_context_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel {
int __gsharp_state = 0;
__gsharp_context_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel* __gsharp_all_contexts = nullptr;
uint64_t __gsharp_warp_exchange[2] = {};
uint32_t __gsharp_warp_shuffle_count = 0;
bool __gsharp_yielded_for_grid_sync = false;
bool __gsharp_yielded_for_block_barrier = false;
};
extern "C" __declspec(dllexport) void __gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel(GSharpArray<int> __gsharp_id_0_input, GSharpArray<int> __gsharp_id_1_output, __gsharp_context_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel* __gsharp_ctx){
#define __gsharp_state __gsharp_ctx->__gsharp_state
#define __gsharp_yielded_for_grid_sync __gsharp_ctx->__gsharp_yielded_for_grid_sync
#define __gsharp_yielded_for_block_barrier __gsharp_ctx->__gsharp_yielded_for_block_barrier
auto* __gsharp_root = __gsharp_ctx;
switch(__gsharp_state)
{
case 0:
__gsharp_switch_1_state_0:
{
int __gsharp_id_2_tid = (int)threadIdx.x;
int __gsharp_id_3_val = __gsharp_id_0_input[__gsharp_id_2_tid];
__gsharp_id_1_output[__gsharp_id_2_tid]=([&]() {
switch(__gsharp_id_3_val)
{
case 1: return 10;
case 2: return 20;
case 3: return 30;
default: return 99;
}
}());
__gsharp_state=-1;
return;
}
}
#undef __gsharp_state
#undef __gsharp_yielded_for_grid_sync
#undef __gsharp_yielded_for_block_barrier
}
extern "C" __declspec(dllexport) void __gsharp_launch_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel(uint32_t __gsharp_grid_dim_x, uint32_t __gsharp_grid_dim_y, uint32_t __gsharp_grid_dim_z, uint32_t __gsharp_block_dim_x, uint32_t __gsharp_block_dim_y, uint32_t __gsharp_block_dim_z, uint32_t __gsharp_shared_mem_bytes, GSharpArray<int> __gsharp_id_0_input, GSharpArray<int> __gsharp_id_1_output)
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
std::vector<__gsharp_context_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel> __gsharp_contexts(__gsharp_block_dim_x * __gsharp_block_dim_y * __gsharp_block_dim_z);
for (auto& __gsharp_launch_ctx : __gsharp_contexts) {
__gsharp_launch_ctx.__gsharp_state = 0;
__gsharp_launch_ctx.__gsharp_all_contexts = __gsharp_contexts.data();
}
std::atomic<bool> __gsharp_any_active{true};
while (__gsharp_any_active && !__gsharp_launch_error.HasError())
{
__gsharp_any_active = false;
int32_t __gsharp_threads_per_block = (int32_t)(__gsharp_block_dim_x * __gsharp_block_dim_y * __gsharp_block_dim_z);
for (int32_t __gsharp_thread_index = 0; __gsharp_thread_index < __gsharp_threads_per_block; __gsharp_thread_index++)
{
if (__gsharp_launch_error.HasError()) continue;
__gsharp_active_error_slot = &__gsharp_launch_error;
int32_t __gsharp_tx = __gsharp_thread_index % __gsharp_block_dim_x;
int32_t __gsharp_ty = (__gsharp_thread_index / __gsharp_block_dim_x) % __gsharp_block_dim_y;
int32_t __gsharp_tz = __gsharp_thread_index / (__gsharp_block_dim_x * __gsharp_block_dim_y);
uint32_t __gsharp_ctx_tid = __gsharp_tz * __gsharp_block_dim_y * __gsharp_block_dim_x + __gsharp_ty * __gsharp_block_dim_x + __gsharp_tx;
if (__gsharp_contexts[__gsharp_ctx_tid].__gsharp_state != -1 && !__gsharp_contexts[__gsharp_ctx_tid].__gsharp_yielded_for_block_barrier && !__gsharp_contexts[__gsharp_ctx_tid].__gsharp_yielded_for_grid_sync)
{
__gsharp_set_thread_idx(__gsharp_tx, __gsharp_ty, __gsharp_tz, __gsharp_bx, __gsharp_by, __gsharp_bz, __gsharp_block_dim_x, __gsharp_block_dim_y, __gsharp_block_dim_z, __gsharp_grid_dim_x, __gsharp_grid_dim_y, __gsharp_grid_dim_z);
__gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__ControlFlowKernels__SwitchExpressionKernel(__gsharp_id_0_input, __gsharp_id_1_output, &__gsharp_contexts[__gsharp_ctx_tid]);
if (__gsharp_contexts[__gsharp_ctx_tid].__gsharp_yielded_for_grid_sync)
{
__gsharp_trap(GSharpErrorCode::InvalidOperation, "G#: Gpu.SyncGrid reached in ControlFlowKernels.SwitchExpressionKernel, which is not [GpuKernel(Cooperative = true)]");
}
if (__gsharp_contexts[__gsharp_ctx_tid].__gsharp_state != -1 && !__gsharp_contexts[__gsharp_ctx_tid].__gsharp_yielded_for_block_barrier && !__gsharp_contexts[__gsharp_ctx_tid].__gsharp_yielded_for_grid_sync)
{
__gsharp_any_active = true;
}
}
}
if (!__gsharp_any_active)
{
for (int32_t __gsharp_thread_index = 0; __gsharp_thread_index < __gsharp_threads_per_block; __gsharp_thread_index++)
{
if (__gsharp_contexts[__gsharp_thread_index].__gsharp_yielded_for_block_barrier)
{
__gsharp_contexts[__gsharp_thread_index].__gsharp_yielded_for_block_barrier = false;
__gsharp_any_active = true;
}
}
}
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
