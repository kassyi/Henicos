'use strict';

// The self-test of the rewrite (queue-rewrite.js, hookAnswer of command-rules.js), part of --self-test: commands that
// Claude Code gets back queued, with or without --gpu, and commands that stay denied, against the test projects and the
// justfile of this checkout.

const os = require('os');
const path = require('path');
const { check, hookAnswer } = require('./command-rules.js');
const { queuedText } = require('./queue-rewrite.js');

const Root = path.resolve(__dirname, '..', '..', '..');
const Queue = 'node scripts/human/job-queue.js run ';

function answer(tool, command, extra = {}) {
  return hookAnswer({ tool_name: tool, tool_input: { command, ...extra }, cwd: Root });
}

/** The words between `run` and `--` of a rewritten command. */
function queueOptions(rewritten) {
  return rewritten.slice(Queue.length, rewritten.indexOf(' -- ')).split(' ');
}

function checkRewrites(assert) {
  // [tool, command, --gpu expected, options other than --gpu and --shell]
  const rewritten = [
    ['Bash', 'dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --no-build', false, []],
    ['Bash', 'dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda/Kassyi.GSharp.Tests.Cuda.csproj', true, []],
    ['Bash', 'dotnet build Henicos.slnx -nodeReuse:false', false, []],
    ['Bash', 'dotnet build X && dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --no-build', false, []],
    // Not split: the test builds again (no --no-build), or a segment is not a build or a test
    ['Bash', 'dotnet build Kassyi.GSharp.Tests.Cuda && dotnet test Kassyi.GSharp.Tests.Cuda', true, []],
    ['Bash', 'cd x && dotnet build A && dotnet test Kassyi.GSharp.Tests.Cuda --no-build', true, []],
    ['Bash', 'dotnet test Kassyi.GSharp.Tests.Cuda --no-build && dotnet build A', true, []],
    ['Bash', 'cd x && dotnet test ../Kassyi.GSharp.Tests.Cpu && dotnet test Kassyi.GSharp.Tests.Cuda', true, []],
    ['Bash', `dotnet test Kassyi.GSharp.Tests.Cpu --filter "A|B" -p:X='it'"'"'s' > tmp/log.txt 2>&1`, false, []],
    ['Bash', 'DiffEngine_Disabled=true dotnet test Kassyi.GSharp.Tests.Cpu', false, []],
    ['Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu --filter FullyQualifiedName~Kassyi.GSharp.Tests.Cuda.X', false, []],
    ['Bash', 'just test', true, []],
    ['Bash', 'just test-differential-cpu', false, []],
    ['Bash', 'just fuzz-diff cpu 5', false, ['--weight', '2']],
    ['PowerShell', `dotnet test Kassyi.GSharp.Tests.Cpu --filter "Name~it's" -p:Y='a b'`, false, []],
    ['PowerShell', 'dotnet build X && dotnet test Kassyi.GSharp.Tests.Cuda', true, []],
    ['PowerShell', 'just test-category Runtime', true, []],
  ];
  for (const [tool, command, gpu, options] of rewritten) {
    const kind = tool === 'PowerShell' ? 'pwsh' : 'bash';
    const output = answer(tool, command).hookSpecificOutput;
    assert.ok(output.updatedInput !== undefined, `should queue in ${kind}: ${command}`);
    const queued = output.updatedInput.command;
    assert.ok(queued.startsWith(Queue), `starts with the queue: ${queued}`);
    assert.deepStrictEqual(check(queued, kind), [], `the queued form passes the hook: ${queued}`);
    assert.strictEqual(queuedText(queued, kind), command, `quoting round-trips in ${kind}: ${queued}`);
    const expected = [...(gpu ? ['--gpu'] : []), ...options, '--shell', kind];
    assert.deepStrictEqual(queueOptions(queued), expected, `queue options of: ${command}`);
  }
  // The whole tool input is kept, only the command changes; no permission decision, so the user is still asked
  const fields = { timeout: 600000, run_in_background: true, description: 'Run', dangerouslyDisableSandbox: false };
  const output = answer('Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu', fields).hookSpecificOutput;
  assert.deepStrictEqual(Object.keys(output).sort(), ['additionalContext', 'hookEventName', 'updatedInput']);
  assert.deepStrictEqual(output.updatedInput, { command: output.updatedInput.command, ...fields });
  assert.ok(output.additionalContext.includes(output.updatedInput.command), 'the context names the queued command');
  // A trailing comment (# intent: ...) stays outside the quotes, where the command gate reads it
  const intent = answer('Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu # intent: run the CPU tests').hookSpecificOutput;
  assert.ok(intent.updatedInput.command.endsWith("Kassyi.GSharp.Tests.Cpu' # intent: run the CPU tests"));
  assert.strictEqual(queuedText(intent.updatedInput.command, 'bash'), 'dotnet test Kassyi.GSharp.Tests.Cpu');

  checkSplitBuilds(assert);

  // Denied as before: the same message, no rewrite
  const plain = [
    ['Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu | tail -5'],
    ['Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu; echo done'],
    ['Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu || echo failed'],
    ['Bash', 'dotnet build X &'],
    ['Bash', 'dotnet build X\ndotnet test Kassyi.GSharp.Tests.Cpu'],
    ['Bash', 'dotnet build \\\n  X'],
    ['Bash', "cat <<'EOF' > a.txt\nx\nEOF\ndotnet build X"],
    ['Bash', 'dotnet test $(echo Kassyi.GSharp.Tests.Cpu)'],
    ['Bash', 'dotnet test `echo Kassyi.GSharp.Tests.Cpu`'],
    ['Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu < input.txt'],
    ['Bash', 'cmd /c "dotnet test Kassyi.GSharp.Tests.Cpu"'],
    ['Bash', 'pwsh -c "dotnet test Kassyi.GSharp.Tests.Cpu"'],
    ['Bash', 'bash -c "dotnet test Kassyi.GSharp.Tests.Cpu"'],
    ['Bash', 'wsl -- dotnet test Kassyi.GSharp.Tests.Cpu'],
    ['Bash', '(dotnet build X)'],
    ['Bash', 'timeout 600 dotnet build X'],
    ['Bash', '"C:\\Program Files\\dotnet\\dotnet.exe" build X'],
    ['Bash', 'node scripts/human/gpu-lock.js -- "dotnet test X"'],
    ['Bash', 'node scripts/human/test-stability.js --count 3'],
    ['Bash', 'dotnet watch test Kassyi.GSharp.Tests.Cpu'],
    ['Bash', 'dotnet stryker'],
    ['Bash', 'cd tmp/worktree/x && just test'],
    ['Bash', 'X=/foo dotnet build X'],
    ['Bash', 'node scripts/human/job-queue.js run -- "dotnet build X" && dotnet test Kassyi.GSharp.Tests.Cpu'],
    ['PowerShell', '$o = dotnet test Kassyi.GSharp.Tests.Cpu'],
    ['PowerShell', '& dotnet test Kassyi.GSharp.Tests.Cpu'],
    ['PowerShell', 'dotnet test Kassyi.GSharp.Tests.Cpu --% -p:X=1'],
    ['PowerShell', "$b = @'\nx\n'@\ndotnet build X"],
  ];
  for (const [tool, command] of plain) {
    const denial = answer(tool, command).hookSpecificOutput;
    assert.strictEqual(denial.updatedInput, undefined, `should not queue: ${command}`);
    assert.strictEqual(denial.permissionDecision, 'deny', `should deny: ${command}`);
    assert.ok(!/--gpu when it does/.test(denial.permissionDecisionReason), `plain denial: ${command}`);
  }
  // A working directory without the queue script (a subdirectory, an old checkout): denied as before
  const elsewhere = hookAnswer({
    tool_name: 'Bash',
    tool_input: { command: 'dotnet build X' },
    cwd: os.tmpdir(),
  }).hookSpecificOutput;
  assert.strictEqual(elsewhere.permissionDecision, 'deny', 'no queue script in the working directory');
  // Antigravity: denied, never rewritten
  const antigravity = hookAnswer({ toolCall: { name: 'run_command', args: { CommandLine: 'dotnet build X' } } });
  assert.strictEqual(antigravity.decision, 'deny', 'Antigravity stays denied');

  // GPU use not decidable: denied, with the reason saying that --gpu may be needed
  const undecided = [
    'dotnet test',
    'dotnet test Henicos.slnx',
    'dotnet test tests/Stdlib/Kassyi.GSharp.Stdlib.GpuMath.Tests',
    'dotnet test Kassyi.GSharp.Tests.Cpu && dotnet test Kassyi.ZeroAlloc.Linq.Tests',
    'dotnet msbuild Henicos.slnx -t:VSTest',
    'just test-memory Kassyi.GSharp.Tests.Cuda',
    'just mutate compiler',
  ];
  for (const command of undecided) {
    const denial = answer('Bash', command).hookSpecificOutput;
    assert.strictEqual(denial.updatedInput, undefined, `should not queue: ${command}`);
    assert.ok(/--gpu when it does/.test(denial.permissionDecisionReason), `should mention --gpu: ${command}`);
  }
}

/**
 * A build and the tests of a GPU project: two queued runs, the builds without --gpu and then the tests with --gpu, so
 * that the GPU turn is not held while the build runs.
 */
function checkSplitBuilds(assert) {
  const cuda = 'tests/Compiler/Kassyi.GSharp.Tests.Cuda';
  // [tool, command, the builds, the tests]
  const split = [
    [
      'Bash',
      `dotnet build ${cuda} -c Release && dotnet test ${cuda} -c Release --no-build --filter "A|B"`,
      `dotnet build ${cuda} -c Release`,
      `dotnet test ${cuda} -c Release --no-build --filter "A|B"`,
    ],
    [
      'Bash',
      `DiffEngine_Disabled=true dotnet build A && dotnet build ${cuda} && ` +
        `dotnet test ${cuda} --no-build && dotnet test Kassyi.GSharp.Tests.Cpu --no-build -p:X='it'"'"'s'`,
      `DiffEngine_Disabled=true dotnet build A && dotnet build ${cuda}`,
      `dotnet test ${cuda} --no-build && dotnet test Kassyi.GSharp.Tests.Cpu --no-build -p:X='it'"'"'s'`,
    ],
    [
      'PowerShell',
      `dotnet build ${cuda} && dotnet test ${cuda} --no-build --filter "Name~it's"`,
      `dotnet build ${cuda}`,
      `dotnet test ${cuda} --no-build --filter "Name~it's"`,
    ],
  ];
  for (const [tool, command, builds, tests] of split) {
    const kind = tool === 'PowerShell' ? 'pwsh' : 'bash';
    const intent = ' # intent: run the GPU tests';
    const output = answer(tool, command + intent).hookSpecificOutput;
    assert.ok(output.updatedInput !== undefined, `should queue in ${kind}: ${command}`);
    const queued = output.updatedInput.command;
    assert.deepStrictEqual(check(queued, kind), [], `the queued form passes the hook: ${queued}`);
    const at = queued.indexOf(` && ${Queue}`);
    assert.ok(queued.startsWith(Queue) && at > 0, `two queued runs joined by &&: ${queued}`);
    const first = queued.slice(0, at);
    const second = queued.slice(at + ' && '.length);
    assert.deepStrictEqual(queueOptions(first), ['--shell', kind], `the builds take no GPU: ${queued}`);
    assert.deepStrictEqual(queueOptions(second), ['--gpu', '--shell', kind], `the tests take the GPU: ${queued}`);
    assert.strictEqual(queuedText(first, kind), builds, `the builds round-trip in ${kind}: ${queued}`);
    assert.ok(second.endsWith(intent), `the intent comment stays at the end, outside the quotes: ${queued}`);
    assert.strictEqual(queuedText(second, kind), tests, `the tests round-trip in ${kind}: ${queued}`);
    assert.ok(/held only for the tests/.test(output.additionalContext), `the agent is told of the split: ${command}`);
  }
}

module.exports = { checkRewrites };
