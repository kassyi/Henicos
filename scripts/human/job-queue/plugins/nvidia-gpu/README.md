# NVIDIA GPU Plugin (`plugins/nvidia-gpu/`)

CJS-only `holderWatch` plugin for `resources.gpu`. This directory has no `plugin.ts`, so `PluginHost` skips it while
`lib/resource-lock.js` loads `holder-watch.js` when `resources.gpu.holderWatch` is set in `job-queue.config.json`.

## Files

| File | Purpose |
| :- | :- |
| `holder-watch.js` | Exports `createWatch({ lockPath, commandText, resource, mode, seat })` for `holds` and `pools` |
| `holds.js` | Samples `nvidia-smi` into `gpu-hold-log.tsv` (12 columns with `mode`) and formats `--holds` |
| `pools.js` | Reads Windows kernel pools and `nvlddmkm` Event 153 counts into `gpu-pool-log.tsv` |
| `fault-watch.js` | Windows WMI / `typeperf` pool reader and Event 153 reader (`gpu-fault-watch.js` target) |

Both `gpu-hold-log.tsv` and `gpu-pool-log.tsv` live next to seat 0 (`path.dirname(lockPath)`).
Set `GSHARP_GPU_HOLD_WATCH=0` or `GSHARP_GPU_POOL_WATCH=0` to disable either watch.

## Configuration

### `holderWatch`

```json
"resources": {
  "gpu": {
    "slots": 2,
    "lock": {
      "seats": ["tmp/gpu-test-lock", "tmp/gpu-test-lock-seat1"]
    },
    "holderWatch": [
      "scripts/human/job-queue/plugins/nvidia-gpu/holder-watch.js"
    ]
  }
}
```

### `probe.argv` Example

When `slots` is raised above `1` to allow concurrent `shared` GPU turns, configure a free-memory probe so a second
shared job only starts while enough GPU memory is free on the first GPU (the same GPU sampled by `holds.js`):

```json
"probe": {
  "argv": ["nvidia-smi", "--query-gpu=memory.free", "--format=csv,noheader,nounits"],
  "min": 3072,
  "timeoutMs": 3000,
  "settleMs": 0
}
```

`probe.argv` queries GPU 0 free memory in MiB; `min: 3072` requires at least 3072 MiB free before admitting another
shared turn, with a 3000 ms command timeout and 0 ms settle delay.
