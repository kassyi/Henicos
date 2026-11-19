'use strict';

// Hook rule, test-project lookup, and full real-config integration checks for the Henicos repository plugin.
//
// Usage: invoked by `node scripts/agent/check-queued-command.js --self-test`.

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const henicosPlugin = require('./hook-rules.js');

const Root = path.resolve(__dirname, '..', '..', '..');
const Queue = 'node scripts/human/job-queue.js run ';

function queueOptions(rewritten) {
  return rewritten.slice(Queue.length, rewritten.indexOf(' -- ')).split(' ');
}

function checkTestProjectGuards(assert) {
  const { testGpu } = henicosPlugin.api;
  const base = {
    list: () => [{ name: 'G' }],
    readErrorMessage: (_root, error) => `could not read tests: ${error.message}`,
    missingMessage: 'it names no test project of tests/',
    usesGpu: () => ({ gpu: true }),
  };
  const failList = () => {
    throw new Error('boom');
  };
  assert.deepStrictEqual(
    testGpu('dotnet test G', Root, { ...base, list: failList }),
    { undecided: 'could not read tests: boom' },
  );
  assert.deepStrictEqual(testGpu('dotnet test', Root, base), { undecided: 'it names no test project of tests/' });
  assert.deepStrictEqual(testGpu('dotnet test G', Root, { ...base, usesGpu: () => null }), {
    undecided: 'GPU use is unknown',
  });
  assert.deepStrictEqual(testGpu('dotnet test G', Root, { ...base, usesGpu: () => ({}) }), {
    undecided: 'GPU use is unknown',
  });
  assert.deepStrictEqual(
    testGpu('dotnet test G', Root, { ...base, usesGpu: () => ({ undecided: 'ambiguous', gpu: true }) }),
    { undecided: 'ambiguous' },
  );
}

function checkRealConfigIntegration(assert, createEngine) {
  const { check, checkDetailed, hookAnswer, denialText } = createEngine({ tolerant: false });
  const { queuedText } = require('../../../scripts/agent/check-queued-command/queue-rewrite.js');

  // The wording of this plugin, not the engine's neutral default, is what the agents read
  assert.strictEqual(denialText(['dotnet build']), henicosPlugin.messages.denialText(['dotnet build']));
  assert.ok(denialText(['dotnet build']).includes('(uses the GPU; replaces gpu-lock.js)'), 'repository wording');

  const denied = [
    'node scripts/human/gpu-lock.js -- "dotnet test X"',
    'node scripts/agent/gpu-lock.js -- "dotnet test X"',
    'node scripts/human/test-stability.js --count 3',
    'node scripts/human/test-stability.js -- --dry-run',
    'node scripts/human/fuzz-differential.js --help',
    'node --max-old-space-size=4096 scripts/human/test-stability.js',
    './scripts/human/long-checks.js',
    'node scripts/human/test-memory.js',
    'node scripts/human/test-sanitized.js',
    'node scripts/human/mutate.js',
    'node scripts/human/coverage-diff.js',
    'cuda-memcheck dotnet test',
    'nsys profile dotnet test',
    'ncu dotnet test',
  ];
  for (const command of denied) {
    assert.notDeepStrictEqual(check(command), [], `henicos real config should deny: ${command}`);
  }

  const allowed = [
    'node scripts/human/job-queue.js run -- "dotnet test X"',
    'node scripts/human/job-queue.js run --gpu -- "just test-category Runtime"',
    'cd tmp/w && node scripts/human/job-queue.js run -- "dotnet build && dotnet test"',
    'node scripts/agent/build-queue.js run -- dotnet test X',
    'node scripts/human/gpu-lock.js --help',
    'node scripts/human/gpu-lock.js --self-test',
    'node --check scripts/human/gpu-lock.js',
    'node scripts/human/gpu-lock.js --lock-path tmp/lock -- "dotnet test X"',
    'node scripts/agent/gpu-lock.js --help',
    'node scripts/human/long-checks.js --dry-run',
    "git commit -m \"$(cat <<'EOF'\nfix: run dotnet test (and just test) first\nEOF\n)\"",
    'node scripts/human/test-memory.js --help',
    'node scripts/human/test-sanitized.js --help',
    'node scripts/human/mutate.js --dry-run',
    'node scripts/human/coverage-diff.js --help',
    'cuda-memcheck --help',
    'nsys --version',
    'ncu --help',
    '"$S/long-checks.js"',
    '"$X/node" scripts/human/long-checks.js',
    'compute-sanitizer dotnet-stryker',
    'valgrind dnx dotnet-stryker',
  ];
  for (const command of allowed) {
    assert.deepStrictEqual(check(command), [], `henicos real config should allow: ${command}`);
  }

  // checkGeneratedForms: test-stability across all 50 non-compute-sanitizer templates + compute-sanitizer across all 7
  const testStability = 'node --max-old-space-size=4096 scripts/human/test-stability.js';
  const templates = {
    runsBash: [
      '{B}',
      'cd x && {B}',
      'ls | {B}',
      '{B} 2>&1 | tail -5',
      'FOO=1 BAR="a b" {B}',
      'LD_PRELOAD=$(g++ -print-file-name=libasan.so) {B}',
      'timeout -s INT 600 {B}',
      'nohup {B} &',
      '( {B} )',
      'time ({B})',
      'if true; then\n  {B}\nfi',
      'for i in 1 2; do {B}; done',
      'f(){ {B}; }; f',
      'x=$({B})',
      'echo "$({B})"',
      'bash -lc "{B}"',
      'bash -c "echo \\"a\\"; {B}"',
      'cmd /c "{B}"',
      'wsl -- bash -lc "{B}"',
      '"$CS" --log-file "a b" {B}',
      'cat <<EOF\ntext\nEOF\n{B}',
      'echo a \\\n  && {B}',
      'git status && {B}',
    ],
    runsPwsh: [
      '{B}',
      'Set-Location "tmp\\"; {B}',
      '$out = {B}',
      '$o = ( {B} )',
      '$r = @({B})',
      'Write-Output "$({B})"',
      'if ($true) { {B} }',
      '1..2 | ForEach-Object { {B} }',
      '& {B}',
      'pwsh -NoProfile -Command "{B}"',
      'pwsh -Command "Write-Output `"a`"; {B}"',
      'cmd.exe /c "{B}"',
      "$b = @'\ntext\n'@\n{B}",
    ],
    namesBash: [
      'echo "{B}"',
      'git commit -m "run {B} first"',
      'git commit -m "$(cat <<\'EOF\'\nfix: run {B} first\nEOF\n)"',
      'grep -n "{B}" justfile',
      "cat > notes.md <<'EOF'\n{B}\nEOF",
      'ls # see; {B}',
      'echo "x\\"; {B}"',
      'msg="{B}"; echo "$msg"',
    ],
    namesPwsh: [
      'Write-Output "{B}"',
      "gh pr comment 1 --body @'\nIt's fine. Ran {B}.\n'@",
      'Select-String -Path justfile -Pattern "{B}"',
      'Get-ChildItem # see; {B}',
      '$msg = "{B}"; Write-Output $msg',
      'Test-Path "src\\x\\"; Write-Output "fix; {B}"',
    ],
  };
  for (const t of templates.runsBash) {
    assert.notDeepStrictEqual(check(t.split('{B}').join(testStability), 'bash'), []);
  }
  for (const t of templates.runsPwsh) {
    assert.notDeepStrictEqual(check(t.split('{B}').join(testStability), 'pwsh'), []);
  }
  for (const t of templates.namesBash) {
    assert.deepStrictEqual(check(t.split('{B}').join(testStability), 'bash'), []);
  }
  for (const t of templates.namesPwsh) {
    assert.deepStrictEqual(check(t.split('{B}').join(testStability), 'pwsh'), []);
  }
  for (const build of [
    'dotnet build',
    'dotnet test tests/X.csproj --filter "A|B"',
    'dotnet -d build -p:Foo="a b"',
    'msbuild Henicos.slnx',
    'just test',
    'just -f justfile test-category Runtime',
    testStability,
  ]) {
    assert.notDeepStrictEqual(
      check(`compute-sanitizer --tool memcheck ${build}`),
      [],
      `compute-sanitizer denies: ${build}`,
    );
  }

  // checkDetailedReasons for henicos nodeScripts
  assert.deepStrictEqual(checkDetailed('node scripts/human/long-checks.js', 'bash'), [
    { tool: 'node-script', verb: 'long-checks.js', text: 'long-checks.js' },
  ]);
  assert.deepStrictEqual(checkDetailed('./scripts/human/long-checks.js', 'bash'), [
    { tool: 'node-script', verb: 'long-checks.js', text: 'long-checks.js' },
  ]);

  const answer = (tool, command, extra = {}) =>
    hookAnswer({ tool_name: tool, tool_input: { command, ...extra }, cwd: Root });

  // All 17 rewritten cases from self-test-rewrite.js
  const rewritten = [
    ['Bash', 'dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --no-build', false, []],
    ['Bash', 'dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda/Kassyi.GSharp.Tests.Cuda.csproj', true, []],
    ['Bash', 'dotnet build Henicos.slnx -nodeReuse:false', false, []],
    ['Bash', 'dotnet build X && dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cpu --no-build', false, []],
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

  const fields = { timeout: 600000, run_in_background: true, description: 'Run', dangerouslyDisableSandbox: false };
  const fieldOut = answer('Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu', fields).hookSpecificOutput;
  assert.deepStrictEqual(Object.keys(fieldOut).sort(), ['additionalContext', 'hookEventName', 'updatedInput']);
  assert.deepStrictEqual(fieldOut.updatedInput, { command: fieldOut.updatedInput.command, ...fields });
  assert.ok(fieldOut.additionalContext.includes(fieldOut.updatedInput.command));

  const intent = answer('Bash', 'dotnet test Kassyi.GSharp.Tests.Cpu # intent: run the CPU tests').hookSpecificOutput;
  assert.ok(intent.updatedInput.command.endsWith("Kassyi.GSharp.Tests.Cpu' # intent: run the CPU tests"));
  assert.strictEqual(queuedText(intent.updatedInput.command, 'bash'), 'dotnet test Kassyi.GSharp.Tests.Cpu');

  // All 3 splitBuilds cases
  const cuda = 'tests/Compiler/Kassyi.GSharp.Tests.Cuda';
  const splitCases = [
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
  for (const [tool, command, builds, tests] of splitCases) {
    const kind = tool === 'PowerShell' ? 'pwsh' : 'bash';
    const intentComment = ' # intent: run the GPU tests';
    const output = answer(tool, command + intentComment).hookSpecificOutput;
    assert.ok(output.updatedInput !== undefined, `split should queue in ${kind}: ${command}`);
    const queued = output.updatedInput.command;
    assert.deepStrictEqual(check(queued, kind), [], `split queued form passes hook: ${queued}`);
    const at = queued.indexOf(` && ${Queue}`);
    assert.ok(queued.startsWith(Queue) && at > 0, `two queued runs joined by &&: ${queued}`);
    const first = queued.slice(0, at);
    const second = queued.slice(at + ' && '.length);
    assert.deepStrictEqual(queueOptions(first), ['--shell', kind]);
    assert.deepStrictEqual(queueOptions(second), ['--gpu', '--shell', kind]);
    assert.strictEqual(queuedText(first, kind), builds);
    assert.ok(second.endsWith(intentComment));
    assert.strictEqual(queuedText(second, kind), tests);
    assert.ok(/held only for the tests/.test(output.additionalContext));
  }

  // All 28 plain denials
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

  const elsewhere = hookAnswer({
    tool_name: 'Bash',
    tool_input: { command: 'dotnet build X' },
    cwd: os.tmpdir(),
  }).hookSpecificOutput;
  assert.strictEqual(elsewhere.permissionDecision, 'deny', 'no queue script in cwd');

  const prevCwd = process.cwd();
  let antigravity;
  try {
    process.chdir(Root);
    antigravity = hookAnswer({ toolCall: { name: 'run_command', args: { CommandLine: 'dotnet build X' } } });
  } finally {
    process.chdir(prevCwd);
  }
  assert.strictEqual(antigravity.decision, 'deny', 'Antigravity stays denied');

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

function checkWithoutHenicosFallsBackToUndecided(assert, createEngine) {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-no-henicos-'));
  try {
    fs.mkdirSync(path.join(tmpDir, 'scripts', 'human'), { recursive: true });
    fs.writeFileSync(path.join(tmpDir, 'scripts', 'human', 'job-queue.js'), '// stub\n');
    const configPath = path.join(tmpDir, 'job-queue.config.json');
    fs.writeFileSync(
      configPath,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [{ name: 'dotnet' }, { name: 'agents' }],
      }),
    );
    const engine = createEngine({ configPath: fs.realpathSync.native(configPath), tolerant: false });
    for (const proj of ['Kassyi.GSharp.Tests.Cpu', 'Kassyi.GSharp.Tests.Cuda']) {
      const out = engine.hookAnswer({
        tool_name: 'Bash',
        tool_input: { command: `dotnet test ${proj}` },
        cwd: fs.realpathSync.native(tmpDir),
      }).hookSpecificOutput;
      assert.strictEqual(out.permissionDecision, 'deny', `without henicos, ${proj} is denied`);
      assert.ok(/--gpu when it does/.test(out.permissionDecisionReason), `without henicos, ${proj} undecided`);
    }
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

function checkCustomTestsDirIsolation(assert, createEngine) {
  const tmpDir = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-testsdir-')));
  try {
    fs.mkdirSync(path.join(tmpDir, 'scripts', 'human'), { recursive: true });
    fs.writeFileSync(path.join(tmpDir, 'scripts', 'human', 'job-queue.js'), '// stub\n');
    const henicosCopy = path.join(tmpDir, 'scripts', 'human', 'job-queue-henicos');
    fs.mkdirSync(henicosCopy, { recursive: true });
    fs.copyFileSync(path.join(__dirname, 'hook-rules.js'), path.join(henicosCopy, 'hook-rules.js'));
    fs.copyFileSync(path.join(__dirname, 'target-dialect.js'), path.join(henicosCopy, 'target-dialect.js'));
    const customProj = path.join(tmpDir, 'custom-tests', 'Cat', 'MyGpu.Tests');
    fs.mkdirSync(customProj, { recursive: true });
    fs.writeFileSync(
      path.join(customProj, 'MyGpu.Tests.csproj'),
      '<Project><PropertyGroup><GSharpTargetDialect>Cuda</GSharpTargetDialect></PropertyGroup></Project>',
    );
    const configPath = path.join(tmpDir, 'job-queue.config.json');
    fs.writeFileSync(
      configPath,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [
          { name: './scripts/human/job-queue-henicos', options: { testsDir: 'custom-tests' } },
          { name: 'dotnet' },
          { name: 'agents' },
        ],
      }),
    );
    const customEngine = createEngine({ configPath: fs.realpathSync.native(configPath), tolerant: false });
    const defaultEngine = createEngine({ tolerant: false });
    const out = customEngine.hookAnswer({
      tool_name: 'Bash',
      tool_input: { command: 'dotnet test MyGpu.Tests' },
      cwd: tmpDir,
    }).hookSpecificOutput;
    assert.ok(
      out.updatedInput && out.updatedInput.command.includes('--gpu --shell bash'),
      'custom testsDir option queues GPU test',
    );
    const defaultOut = defaultEngine.hookAnswer({
      tool_name: 'Bash',
      tool_input: { command: 'dotnet test Kassyi.GSharp.Tests.Cpu' },
      cwd: Root,
    }).hookSpecificOutput;
    assert.ok(
      defaultOut.updatedInput && !defaultOut.updatedInput.command.includes('--gpu'),
      'default engine testsDir unaffected by custom engine instance',
    );
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

function checks(assert, { createEngine }) {
  assert.throws(() => henicosPlugin.rules(123), /options must be an object/);
  assert.throws(() => henicosPlugin.rules({ testsDir: '' }), /testsDir must be a string/);
  checkTestProjectGuards(assert);
  checkRealConfigIntegration(assert, createEngine);
  checkWithoutHenicosFallsBackToUndecided(assert, createEngine);
  checkCustomTestsDirIsolation(assert, createEngine);
}

module.exports = { checks };
