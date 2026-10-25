#ifndef __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod__
#define __GSHARP_STRUCT___gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod__
struct __gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod
{
volatile int __gsharp_mb_Flag;
}
;
static_assert(sizeof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod)==4,"Kassyi.GSharp.Tests.Shared.Syntax.VolatileState_MicroMod: size");
static_assert(alignof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod)==4,"Kassyi.GSharp.Tests.Shared.Syntax.VolatileState_MicroMod: alignment");
static_assert(offsetof(__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod,__gsharp_mb_Flag)==0,"Kassyi.GSharp.Tests.Shared.Syntax.VolatileState_MicroMod.Flag: offset");
#endif
extern "C" __declspec(dllexport) void __gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__MicroModifiersTarget__VolatileModifier(GSharpArray<__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod> __gsharp_id_0_states){
__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod __gsharp_id_1_s = __gsharp_id_0_states[0];
__gsharp_id_1_s.__gsharp_mb_Flag=1;
__gsharp_id_0_states[0]=__gsharp_id_1_s;
}
extern "C" __declspec(dllexport) void __gsharp_launch_Kassyi__GSharp__Tests__Shared__Syntax__MicroModifiersTarget__VolatileModifier(uint32_t __gsharp_grid_dim_x, uint32_t __gsharp_grid_dim_y, uint32_t __gsharp_grid_dim_z, uint32_t __gsharp_block_dim_x, uint32_t __gsharp_block_dim_y, uint32_t __gsharp_block_dim_z, GSharpArray<__gsharp_ty_Kassyi__GSharp__Tests__Shared__Syntax__VolatileState_uMicroMod> __gsharp_id_0_states)
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
__gsharp_k_Kassyi__GSharp__Tests__Shared__Syntax__MicroModifiersTarget__VolatileModifier(__gsharp_id_0_states);
}
}
if (__gsharp_launch_error.HasError()) {
__gsharp_host_last_error.Report(static_cast<GSharpErrorCode>(__gsharp_launch_error.code.load(std::memory_order_acquire)), __gsharp_launch_error.message);
}
}
