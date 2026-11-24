// Self-tests of the dotnet plugin: build/test split via PluginHost, MSBuild node limits, QueuedRun wire keys &
// JOB_QUEUE_PLUGINS=0, classify / shortName / api helpers, plus characterization and remote offload checks.

import * as path from "node:path";
import { CpusVariable, EntryScript, JobVariable, ProtocolVersion, type ShellName } from "../../constants.ts";
import { runBuildStep } from "../../client-build-step.ts";
import { QueuedRun } from "../../client-queued-run.ts";
import type { RunOptions } from "../../client.ts";
import { loadConfig } from "../../config.ts";
import { commandKeys } from "../../estimate-key.ts";
import { resolvePlaces } from "../../path-places.ts";
import type { AssertModule } from "../../plugin-api.ts";
import { PluginHost, type BaseClientContext } from "../../plugin-host.ts";
import { FakeHost, FakeScheduler, quietly, until } from "../../self-test-client.ts";
import { resolveWorktree } from "../../system.ts";
import { shortCommand } from "../../view.ts";
import { checkCharacterization, checkDotnetEstimateAndPlaces } from "./characterization-checks.ts";
import dotnetPlugin, { isDotnetTest, selectsCategory } from "./plugin.ts";
import { checkDotnetRemoteOffload } from "./remote-checks.ts";

const Cpu = "tests/Compiler/App.Cpu";
const CudaFull = "tests/Compiler/Kassyi.GSharp.Tests.Cuda";

function clientContext(shell: ShellName = "bash", configRoot = process.cwd()): BaseClientContext {
  return { cwd: configRoot, worktree: configRoot, configRoot, shell, env: {}, log: () => {} };
}

function baseRunOptions(command: string): RunOptions {
  return {
    port: 0,
    slots: 2,
    memoryMb: 1024,
    policy: "hrrn",
    command,
    commandWords: null,
    weight: 1,
    gpu: false,
    shell: "",
    timeoutMinutes: 0,
    after: 0,
    keepIfFailed: false,
  };
}

/** Moved from self-test-build-split.ts (all 42 cases) through PluginHost.splitBeforeTurn. */
function checkSplitBeforeTurn(assert: AssertModule, host: PluginHost): void {
  const gpu = { name: "gpu", forcedExclusive: false };
  const planned: [ShellName, string, string, string][] = [
    ["bash", `dotnet test ${CudaFull}`, `dotnet build ${CudaFull}`, `dotnet test --no-build ${CudaFull}`],
    [
      "bash",
      `dotnet test ${CudaFull} -c Release -f net10.0 -p:X=1 --filter "A|B" --logger trx --blame-crash -nodeReuse:false`,
      `dotnet build ${CudaFull} -c Release -f net10.0 -p:X=1 -nodeReuse:false`,
      `dotnet test --no-build ${CudaFull} -c Release -f net10.0 -p:X=1 --filter "A|B" --logger trx --blame-crash ` +
        "-nodeReuse:false",
    ],
    [
      "bash",
      `dotnet build ${CudaFull} -nodeReuse:false && dotnet test ${CudaFull} --no-build --filter X`,
      `dotnet build ${CudaFull} -nodeReuse:false`,
      `dotnet test ${CudaFull} --no-build --filter X`,
    ],
    [
      "bash",
      `dotnet build A && dotnet test ${CudaFull} --configuration=Debug -- RunConfiguration.X=1`,
      `dotnet build A && dotnet build ${CudaFull} --configuration=Debug`,
      `dotnet test --no-build ${CudaFull} --configuration=Debug -- RunConfiguration.X=1`,
    ],
    [
      "pwsh",
      `dotnet test ${CudaFull} --filter "Name~it's" && dotnet test ${CudaFull} -c Release`,
      `dotnet build ${CudaFull} && dotnet build ${CudaFull} -c Release`,
      `dotnet test --no-build ${CudaFull} --filter "Name~it's" && dotnet test --no-build ${CudaFull} -c Release`,
    ],
    [
      "cmd",
      `dotnet test "${CudaFull}" -p:Y=%Z%`,
      `dotnet build "${CudaFull}" -p:Y=%Z%`,
      `dotnet test --no-build "${CudaFull}" -p:Y=%Z%`,
    ],
    [
      "bash",
      `dotnet build ${CudaFull} -nodeReuse:false -v:q -clp:ErrorsOnly > tmp/build-cuda.log 2>&1 && ` +
        `DiffEngine_Disabled=true dotnet test ${CudaFull} --no-build --filter 'X' > tmp/test-cuda.log 2>&1`,
      `dotnet build ${CudaFull} -nodeReuse:false -v:q -clp:ErrorsOnly > tmp/build-cuda.log 2>&1`,
      `DiffEngine_Disabled=true dotnet test ${CudaFull} --no-build --filter 'X' > tmp/test-cuda.log 2>&1`,
    ],
    [
      "bash",
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet test ${CudaFull} -v:q -clp:ErrorsOnly &> "tmp/t.log"`,
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet build ${CudaFull} -v:q -clp:ErrorsOnly &> "tmp/t.log"`,
      `export DiffEngine_Disabled=true CI=1; A="b c" dotnet test --no-build ${CudaFull} -v:q -clp:ErrorsOnly ` +
        `&>> "tmp/t.log"`,
    ],
    [
      "bash",
      `dotnet test ${CudaFull} --filter X >> tmp/a.log 2> tmp/err.log`,
      `dotnet build ${CudaFull} >> tmp/a.log 2> tmp/err.log`,
      `dotnet test --no-build ${CudaFull} --filter X >> tmp/a.log 2>> tmp/err.log`,
    ],
    [
      "pwsh",
      `$env:DiffEngine_Disabled='true'; dotnet build ${CudaFull} *> tmp/b.log && ` +
        `dotnet test ${CudaFull} --no-build 2>&1 > x.log`,
      `$env:DiffEngine_Disabled='true'; dotnet build ${CudaFull} *> tmp/b.log`,
      `$env:DiffEngine_Disabled='true'; dotnet test ${CudaFull} --no-build 2>&1 > x.log`,
    ],
    [
      "pwsh",
      `dotnet test ${CudaFull} -c Release *> 'tmp/t.log'`,
      `dotnet build ${CudaFull} -c Release *> 'tmp/t.log'`,
      `dotnet test --no-build ${CudaFull} -c Release *>> 'tmp/t.log'`,
    ],
    [
      "cmd",
      `dotnet test ${CudaFull} > t.log 2>&1`,
      `dotnet build ${CudaFull} > t.log 2>&1`,
      `dotnet test --no-build ${CudaFull} >> t.log 2>&1`,
    ],
  ];
  for (const [shell, command, before, after] of planned) {
    assert.deepStrictEqual(
      host.splitBeforeTurn(command, gpu, clientContext(shell)),
      { before, after, beforeRole: "build, no GPU", afterRole: "tests, GPU turn" },
      `${shell}: ${command}`,
    );
  }
  const kept: [ShellName, string][] = [
    ["bash", `dotnet test ${CudaFull} --no-build`],
    ["bash", "dotnet test"],
    ["bash", "dotnet test --filter X"],
    ["bash", "dotnet test bin/x.Tests.dll"],
    ["bash", `dotnet test ${CudaFull} --unknown-option value`],
    ["bash", `dotnet test --unknown-switch ${CudaFull}`],
    ["bash", `dotnet test ${CudaFull} | tail -5`],
    ["bash", `dotnet test ${CudaFull}; echo done`],
    ["bash", `dotnet test ${CudaFull} || echo failed`],
    ["bash", `dotnet test $(echo ${CudaFull})`],
    ["bash", `dotnet test ${CudaFull} < input.txt`],
    ["bash", `dotnet test ${CudaFull} > log.txt | tee x`],
    ["bash", `dotnet test ${CudaFull} > $(date).log`],
    ["bash", `dotnet test ${CudaFull} >&2`],
    ["bash", `dotnet test ${CudaFull} > log.txt &`],
    ["bash", `cd x && dotnet test ${CudaFull}`],
    ["bash", `FOO=$(pwd) dotnet test ${CudaFull}`],
    ["bash", `echo x; dotnet test ${CudaFull}`],
    ["bash", `export A=$(pwd); dotnet test ${CudaFull}`],
    ["pwsh", `dotnet test ${CudaFull} | Out-File x`],
    ["pwsh", `$env:A = $b; dotnet test ${CudaFull}`],
    ["pwsh", `FOO=1 dotnet test ${CudaFull}`],
    ["bash", `dotnet test ${CudaFull} && dotnet build A`],
    ["bash", `dotnet test ${CudaFull} -p:X="a\\"b"`],
    ["bash", `dotnet test ${CudaFull}\ndotnet build A`],
    ["bash", "just test"],
    ["bash", "dotnet build A"],
    ["pwsh", `dotnet test ${CudaFull} --% -p:X=1`],
    ["pwsh", `dotnet test ${CudaFull} -p:X=$y`],
    ["pwsh", `& dotnet test ${CudaFull}`],
  ];
  for (const [shell, command] of kept) {
    assert.strictEqual(
      host.splitBeforeTurn(command, gpu, clientContext(shell)),
      undefined,
      `kept as it is in ${shell}: ${command}`,
    );
  }
  assert.strictEqual(
    host.splitBeforeTurn(`dotnet test ${CudaFull}`, { name: "cpu", forcedExclusive: false }, clientContext("bash")),
    undefined,
    "non-gpu resource is not split",
  );
}

/** Moved from self-test.ts (10 limitMsbuildNodes assertions) through PluginHost.prepareCommand. */
function checkPrepareCommand(assert: AssertModule, host: PluginHost): void {
  const ctx = clientContext();
  const cases: [string, string][] = [
    ["dotnet test X.csproj && dotnet build", "dotnet test -m:6 X.csproj && dotnet build -m:6"],
    ["dotnet build-server shutdown", "dotnet build-server shutdown"],
    ["dotnet test -m:2", "dotnet test -m:2"],
    ["dotnet build -m:2 && dotnet test", "dotnet build -m:2 && dotnet test -m:6"],
    ["dotnet build --maxcpucount 4", "dotnet build --maxcpucount 4"],
    ["dotnet.exe build", "dotnet.exe build -m:6"],
    ['"C:\\Program Files\\dotnet\\dotnet.exe" test x', '"C:\\Program Files\\dotnet\\dotnet.exe" test -m:6 x'],
    ["dotnet run --project x", "dotnet run --project x"],
    ['dotnet test --filter "A|B" -m:2', 'dotnet test --filter "A|B" -m:2'],
    ["dotnet test bin/x.Tests.dll", "dotnet test bin/x.Tests.dll"],
  ];
  for (const [input, expected] of cases) {
    const prepared = host.prepareCommand(input, { jobId: 1, cpus: 6, nested: false }, ctx);
    assert.strictEqual(prepared.command, expected, input);
    assert.strictEqual(prepared.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, "-m:6");
    assert.strictEqual(prepared.note, "6 MSBuild nodes");
    // RequestCores follows the grant with or without -m, build/test/run alike: nodes executing + cores granted <= 6
    assert.strictEqual(prepared.env.MSBUILDCORELIMIT, "6", input);
    assert.strictEqual(prepared.env.MSBUILDNODECOREALLOCATIONWEIGHT, "100", input);
  }
  // A value the caller exported is kept; the other one is still set
  const exported = { ...ctx, env: { MSBUILDCORELIMIT: "2" } };
  const kept = host.prepareCommand("dotnet build X", { jobId: 1, cpus: 6, nested: false }, exported);
  assert.strictEqual(kept.env.MSBUILDCORELIMIT, undefined, "exported MSBUILDCORELIMIT is not overridden");
  assert.strictEqual(kept.env.MSBUILDNODECOREALLOCATIONWEIGHT, "100");
  // Any command gets the bound, so a script that starts dotnet itself passes it on
  for (const input of ["node scripts/human/test-stability.js --count 3", "echo hi"]) {
    const other = host.prepareCommand(input, { jobId: 1, cpus: 6, nested: false }, ctx);
    assert.strictEqual(other.env.MSBUILDCORELIMIT, "6", input);
    assert.strictEqual(other.env.MSBUILDNODECOREALLOCATIONWEIGHT, "100", input);
  }
  const zero = host.prepareCommand("dotnet build X", { jobId: 1, cpus: 0, nested: false }, ctx);
  assert.strictEqual(zero.command, "dotnet build X");
  assert.strictEqual(zero.note, undefined);
  assert.strictEqual(zero.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, undefined);
  assert.strictEqual(zero.env.MSBUILDCORELIMIT, undefined);
}

/** QueuedRun + FakeScheduler + FakeHost: request/adopt keys, MSBuild args/note, and JOB_QUEUE_PLUGINS=0 omission. */
async function checkQueuedRunKeysAndEscape(assert: AssertModule, host: PluginHost): Promise<void> {
  const command = `dotnet build ${Cpu} -c Release`;
  const scheduler = new FakeScheduler();
  await scheduler.listen();
  const prevMsbuildArgs = process.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS;
  delete process.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS;
  try {
    // 1. With dotnet plugin active: request sends keys, start adds -m / MSBUILD_ARGS / note, adopt repeats keys
    const fakeHost = new FakeHost();
    const notices: string[] = [];
    const write = process.stderr.write;
    process.stderr.write = ((chunk: string | Uint8Array) =>
      notices.push(String(chunk)) > 0) as typeof process.stderr.write;
    try {
      void new QueuedRun(
        baseRunOptions(command),
        { weight: 1, pin: null, before: null },
        false,
        false,
        fakeHost,
        host,
      ).run();
      await until("dotnet client connects", () => fakeHost.connects.length === 1);
      fakeHost.connects[0]?.resolve(await scheduler.connect());
      await until("scheduler accepts dotnet client", () => scheduler.accepted.length === 1);
      const first = scheduler.accepted[0];
      assert.ok(first !== undefined);
      await until("request arrives", () => first.messages.some((m) => m.type === "request"));
      const req = first.messages.find((m) => m.type === "request");
      const cwd = process.cwd();
      const worktree = resolveWorktree(cwd);
      const places = resolvePlaces(command, { cwd, worktree }, host);
      const expectedKeys = commandKeys(command, { cwd, worktree, places }, host);
      assert.deepStrictEqual(req?.keys, expectedKeys, "request carries plugin-computed keys");

      FakeScheduler.send(first, { type: "queued", id: 10, state: "waiting", ahead: 0, position: 1 });
      FakeScheduler.send(first, { type: "granted", id: 10, cpus: 3, capacity: 4, protocol: ProtocolVersion });
      await until("granted command starts", () => fakeHost.commands.length === 1);
      assert.strictEqual(fakeHost.commands[0]?.command, `dotnet build -m:3 ${Cpu} -c Release`);
      assert.strictEqual(fakeHost.commands[0]?.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, "-m:3");
      assert.strictEqual(fakeHost.commands[0]?.env.MSBUILDCORELIMIT, "3");
      assert.ok(
        notices.some((n) => n.includes("(weight 1, 3 MSBuild nodes)")),
        "grant line uses MSBuild nodes note",
      );

      // Reconnect after scheduler restart -> adopt carries identical keys
      first.socket.destroy();
      await until("dotnet client reconnects", () => fakeHost.connects.length === 2);
      fakeHost.connects[1]?.resolve(await scheduler.connect());
      await until("scheduler accepts reconnect", () => scheduler.accepted.length === 2);
      const second = scheduler.accepted[1];
      assert.ok(second !== undefined);
      await until("adopt arrives", () => second.messages.some((m) => m.type === "adopt"));
      const adopt = second.messages.find((m) => m.type === "adopt");
      assert.deepStrictEqual(adopt?.keys, req?.keys, "adopt sends identical keys as request");

      fakeHost.commands[0]?.end({ code: 0, signaled: false });
      await until("done arrives", () => second.messages.some((m) => m.type === "done"));
      FakeScheduler.send(second, { type: "done-ok", id: 10 });
      await until("dotnet client exits", () => fakeHost.exits.length > 0);
      assert.deepStrictEqual(fakeHost.exits, [0]);
    } finally {
      process.stderr.write = write;
    }

    // 2. JOB_QUEUE_PLUGINS=0: request & adopt omit keys, command has no -m, env omits MSBUILD_ARGS, note uses cpus
    const loaded = loadConfig({ mode: "status", fromDirectory: path.dirname(EntryScript), warn: () => {} });
    const disabledHost = await PluginHost.load(loaded.config, loaded.configRoot, () => {}, {
      ...process.env,
      JOB_QUEUE_PLUGINS: "0",
    });
    assert.strictEqual(disabledHost.plugins.length, 0);
    const noPlugHost = new FakeHost();
    const noPlugNotices: string[] = [];
    const write2 = process.stderr.write;
    process.stderr.write = ((chunk: string | Uint8Array) =>
      noPlugNotices.push(String(chunk)) > 0) as typeof process.stderr.write;
    try {
      void new QueuedRun(
        baseRunOptions(command),
        { weight: 1, pin: null, before: null },
        false,
        false,
        noPlugHost,
        disabledHost,
      ).run();
      await until("disabled client connects", () => noPlugHost.connects.length === 1);
      noPlugHost.connects[0]?.resolve(await scheduler.connect());
      await until("scheduler accepts disabled client", () => scheduler.accepted.length === 3);
      const third = scheduler.accepted[2];
      assert.ok(third !== undefined);
      await until("disabled request arrives", () => third.messages.some((m) => m.type === "request"));
      const req = third.messages.find((m) => m.type === "request");
      assert.strictEqual(req !== undefined && "keys" in req, false, "JOB_QUEUE_PLUGINS=0 omits keys from request");

      FakeScheduler.send(third, { type: "queued", id: 11, state: "waiting", ahead: 0, position: 1 });
      FakeScheduler.send(third, { type: "granted", id: 11, cpus: 3, capacity: 4, protocol: ProtocolVersion });
      await until("disabled command starts", () => noPlugHost.commands.length === 1);
      assert.strictEqual(noPlugHost.commands[0]?.command, command, "JOB_QUEUE_PLUGINS=0 leaves -m out");
      assert.strictEqual(noPlugHost.commands[0]?.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, undefined);
      assert.strictEqual(noPlugHost.commands[0]?.env.MSBUILDCORELIMIT, undefined);
      assert.ok(
        noPlugNotices.some((n) => n.includes("(weight 1, 3 cpus)")),
        "disabled grant line falls back to cpus",
      );

      third.socket.destroy();
      await until("disabled client reconnects", () => noPlugHost.connects.length === 2);
      noPlugHost.connects[1]?.resolve(await scheduler.connect());
      await until("scheduler accepts disabled reconnect", () => scheduler.accepted.length === 4);
      const fourth = scheduler.accepted[3];
      assert.ok(fourth !== undefined);
      await until("disabled adopt arrives", () => fourth.messages.some((m) => m.type === "adopt"));
      const adopt = fourth.messages.find((m) => m.type === "adopt");
      assert.strictEqual(adopt !== undefined && "keys" in adopt, false, "JOB_QUEUE_PLUGINS=0 omits keys from adopt");

      noPlugHost.commands[0]?.end({ code: 0, signaled: false });
      await until("disabled done arrives", () => fourth.messages.some((m) => m.type === "done"));
      FakeScheduler.send(fourth, { type: "done-ok", id: 11 });
      await until("disabled client exits", () => noPlugHost.exits.length > 0);
      assert.deepStrictEqual(noPlugHost.exits, [0]);
    } finally {
      process.stderr.write = write2;
    }

    // 3. Nested runBuildStep applies prepareCommand when JobVariable and CpusVariable are set
    const prevJob = process.env[JobVariable];
    const prevCpus = process.env[CpusVariable];
    process.env[JobVariable] = "99";
    process.env[CpusVariable] = "4";
    try {
      const nestedHost = new FakeHost();
      const pending = runBuildStep(
        baseRunOptions("dotnet build P"),
        { weight: 1, pin: null, before: null },
        "dotnet build P",
        nestedHost,
        "build, no GPU",
        host,
      );
      await until("nested build starts", () => nestedHost.commands.length === 1);
      assert.strictEqual(nestedHost.commands[0]?.command, "dotnet build -m:4 P");
      assert.strictEqual(nestedHost.commands[0]?.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS, "-m:4");
      assert.strictEqual(nestedHost.commands[0]?.env.MSBUILDCORELIMIT, "4");
      nestedHost.commands[0]?.end({ code: 0, signaled: false });
      assert.strictEqual(await pending, 0);
    } finally {
      if (prevJob === undefined) {
        delete process.env[JobVariable];
      } else {
        process.env[JobVariable] = prevJob;
      }
      if (prevCpus === undefined) {
        delete process.env[CpusVariable];
      } else {
        process.env[CpusVariable] = prevCpus;
      }
    }
  } finally {
    if (prevMsbuildArgs !== undefined) {
      process.env.GSHARP_JOB_QUEUE_MSBUILD_ARGS = prevMsbuildArgs;
    }
    scheduler.close();
  }
}

/** Unit checks for classify, shortName, and api ({ isDotnetTest, selectsCategory }). */
function checkClassifyShortNameAndApi(assert: AssertModule): void {
  assert.deepStrictEqual(dotnetPlugin.classify("dotnet test App.csproj"), { test: true });
  assert.deepStrictEqual(dotnetPlugin.classify("dotnet vstest App.dll"), { test: true });
  assert.deepStrictEqual(dotnetPlugin.classify("dotnet tool run dotnet-stryker"), { test: true });
  assert.deepStrictEqual(dotnetPlugin.classify('"C:\\dotnet\\dotnet.exe" test App'), { test: true });
  assert.strictEqual(dotnetPlugin.classify("dotnet build App.csproj"), undefined);
  assert.strictEqual(dotnetPlugin.classify("just test"), undefined);

  assert.strictEqual(
    dotnetPlugin.shortName("dotnet build -nodeReuse:false App", "dotnet build -nodeReuse:false App"),
    "build App",
  );
  assert.strictEqual(
    dotnetPlugin.shortName(
      "dotnet.exe test App -nodeReuse:false && dotnet build B",
      "dotnet.exe test App -nodeReuse:false && dotnet build B",
    ),
    "test App && build B",
  );
  assert.strictEqual(dotnetPlugin.shortName("dotnet build; dotnet test", "dotnet build; dotnet test"), "build ; test");
  assert.strictEqual(
    dotnetPlugin.shortName("dotnet.exe build; dotnet.exe test", "dotnet.exe build; dotnet.exe test"),
    "build ; test",
  );
  assert.strictEqual(dotnetPlugin.shortName("dotnet build;", "dotnet build;"), "build");
  assert.strictEqual(dotnetPlugin.shortName("just test", "just test"), undefined);
  assert.strictEqual(dotnetPlugin.shortName("dotnet", "dotnet"), undefined);
  assert.strictEqual(dotnetPlugin.shortName("-nodeReuse:false", "-nodeReuse:false"), undefined);
  // Without plugins, core generic stripping keeps dotnet and -nodeReuse:false
  assert.strictEqual(
    shortCommand("CI=true dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu -nodeReuse:false > a.log"),
    "dotnet test Kassyi.GSharp.Tests.Cpu -nodeReuse:false",
  );

  assert.strictEqual(isDotnetTest("dotnet test X"), true);
  assert.strictEqual(isDotnetTest("dotnet build X"), false);
  assert.strictEqual(selectsCategory('dotnet test --filter "Category=Stress"', ["Stress", "Benchmark"]), true);
  assert.strictEqual(
    selectsCategory('dotnet test --filter "Category!=Stress&Category!=Benchmark"', ["Stress", "Benchmark"]),
    false,
  );
  assert.strictEqual(
    selectsCategory('dotnet test --filter "Category!=Stress|Category=GpuTrap"', ["Stress", "GpuTrap"]),
    true,
  );
  // A negative term drops one word: what follows it (a quote, a dotted rest) still selects, on the side of caution
  assert.strictEqual(selectsCategory('dotnet test --filter "Category!=Foo.Bar"', ["Bar"]), true);
  assert.strictEqual(selectsCategory("dotnet test --filter \"Category!='Stress'\"", ["Stress"]), true);
  assert.strictEqual(selectsCategory('dotnet test --filter "Category!=Foo.Bar|Category=Stress"', ["Stress"]), true);
  assert.strictEqual(selectsCategory('dotnet test --filter "Category=Stressful"', ["Stress"]), false);
  assert.strictEqual(selectsCategory("dotnet test", ["Stress"]), false);
  assert.strictEqual(selectsCategory("dotnet test Stress", []), false);
  // The same reading as the Stress budget of a test run (fixed pre-move characterization baseline)
  const expectedStress: [string, boolean][] = [
    ["Category=Stress", true],
    ["Category!=Stress", false],
    ["Category!~Benchmark|Category=Benchmark", true],
    ["Category != Stress&Category!=Benchmark", false],
    ["Category!='Stress'", true],
    ["Category!=Foo-Stress", true],
    ["FullyQualifiedName~Stressful", false],
    ["Category=Benchmark.Long", true],
  ];
  for (const [filter, expected] of expectedStress) {
    const command = `dotnet test App --filter "${filter}"`;
    assert.strictEqual(
      selectsCategory(command, ["Stress", "Benchmark"]),
      expected,
      `selectsCategory reads ${filter} as the budget does`,
    );
  }
}

export async function checks(assert: AssertModule): Promise<void> {
  const configWarnings: string[] = [];
  const loaded = loadConfig({
    mode: "status",
    fromDirectory: path.dirname(EntryScript),
    warn: (m) => configWarnings.push(m),
  });
  assert.deepStrictEqual(configWarnings, []);
  const loadWarnings: string[] = [];
  const host = await PluginHost.load(loaded.config, loaded.configRoot, (m) => loadWarnings.push(m), {
    ...process.env,
    JOB_QUEUE_PLUGINS: undefined,
  });
  assert.deepStrictEqual(loadWarnings, []);
  assert.ok(
    host.plugins.some((p) => p.name === "dotnet"),
    "dotnet plugin loaded from repo config",
  );

  checkCharacterization(assert, host);
  checkDotnetEstimateAndPlaces(assert, host);
  checkSplitBeforeTurn(assert, host);
  checkPrepareCommand(assert, host);
  await quietly(() => checkQueuedRunKeysAndEscape(assert, host));
  checkClassifyShortNameAndApi(assert);
  const dotnetOnlyHost = new PluginHost([dotnetPlugin], [{}], loaded.configRoot, () => {});
  await checkDotnetRemoteOffload(assert, dotnetOnlyHost);
}
