'use strict';

// The self-test of check-queued-command.js (--self-test): commands that must be denied and commands that must pass,
// per dialect, written out and generated from templates.

const fs = require('fs');
const os = require('os');
const path = require('path');
const projectHenicos = require('./project-henicos.js');
const { createRules } = require('./command-rules.js');
const { createHook } = require('./hook.js');
const { check, checkDetailed } = createRules(projectHenicos);
const shellReader = require('./shell-reader.js');

function selfTest() {
  const assert = require('assert');
  const denied = [
    'dotnet test tests/X.csproj',
    'cd tmp/w && dotnet build Henicos.slnx -nodeReuse:false',
    'DiffEngine_Disabled=true dotnet test',
    'just test --filter Foo > tmp/log.txt 2>&1',
    'just test-category Runtime',
    'just -f justfile test',
    'node scripts/human/gpu-lock.js -- "dotnet test X"',
    'node scripts/agent/gpu-lock.js -- "dotnet test X"', // the path of older checkouts
    'node scripts/human/test-stability.js --count 3',
    '& dotnet test',
    '"C:\\Program Files\\dotnet\\dotnet.exe" build',
    'echo start; dotnet msbuild -t:Restore',
    'git status && dotnet test',
    'echo job-queue.js; dotnet test',
    'timeout 600 dotnet test',
    'nohup dotnet build &',
    '(dotnet build)',
    'cmd /c "dotnet test x"',
    'pwsh -c "dotnet build"',
    'bash -c "cd x && just test"',
    'Start-Process dotnet -ArgumentList "test x" -Wait',
    'dotnet watch test',
    'dotnet -p:Foo="a b" test',
    'just args="a b" test',
    'FOO="a b" dotnet test',
    '(just test)',
    '(msbuild)',
    'dotnet tool run dotnet-stryker',
    'dotnet tool exec dotnet-stryker',
    'dotnet dotnet-stryker',
    'dotnet dnx dotnet-stryker',
    'dnx dotnet-stryker',
    'dnx dotnet-stryker@4.0.0',
    'dotnet dnx --yes dotnet-stryker@4.0.0',
    'dotnet tool exec dotnet-stryker@4.0.0',
    'dotnet tool run --tool-manifest m.json dotnet-stryker',
    'dnx --source https://x dotnet-stryker',
    'dotnet tool run dotnet-stryker --help',
    'dotnet-stryker --since main',
    'node scripts/human/test-stability.js -- --dry-run',
    'node scripts/human/fuzz-differential.js --help',
    'nice -n 10 dotnet build',
    'stdbuf -oL dotnet test',
    'env -C tmp dotnet test',
    'do { dotnet test } while ($false)',
    'dotnet publish -c Release',
    'dotnet vstest x.dll',
    'msbuild Henicos.slnx',
    "cat > tmp/a.md <<'EOF'\nnot a command\nEOF\ndotnet test",
    'Set-Location x; dotnet test',
    'dotnet \\\n  test --filter X',
    'dotnet `\r\n  build x',
    'powershell -NoProfile -Command "dotnet test"',
    'pwsh -NoProfile -c "dotnet build"',
    'bash -lc "dotnet test"',
    'cmd.exe /d /c "dotnet test"',
    'if true; then dotnet test; fi',
    'for p in a b; do dotnet build $p; done',
    'if ($x) { dotnet test }',
    'foreach ($p in $list) { dotnet build $p }',
    'dotnet stryker',
    'echo $(dotnet test)',
    'x=$(dotnet build)',
    'node --max-old-space-size=4096 scripts/human/test-stability.js',
    './scripts/human/long-checks.js',
    'Start-Process dotnet "test x"',
    'dotnet -d build',
    'sudo dotnet test',
    'ls | xargs -n1 dotnet build',
    'Invoke-Expression "dotnet test"',
    '! dotnet build',
    'node scripts/human/test-memory.js',
    'node scripts/human/test-sanitized.js',
    'node scripts/human/mutate.js',
    'node scripts/human/coverage-diff.js',
    'cuda-memcheck dotnet test',
    'valgrind dotnet test',
    'strace dotnet build',
    'ltrace dotnet build',
    'nsys profile dotnet test',
    'ncu dotnet test',
    'drmemory -- dotnet test',
    'perf record dotnet test',
    'gdb --args dotnet test',
    'lldb -- dotnet test',
    'dotnet-trace collect -- dotnet test',
    'dotnet-counters collect -- dotnet test',
    'dotnet-coverage collect dotnet test',
    'procdump -x . dotnet test',
    '@( dotnet test )',
    'exec dotnet test',
    'command dotnet build',
    'if dotnet test; then echo ok; fi',
    'if false; then echo ok; elif dotnet test; then echo ok; fi',
    'if false; then echo ok; else dotnet test; fi',
    'while dotnet test; do echo ok; done',
    'until dotnet test; do echo ok; done',
    'just coverage-diff',
    'just long-checks',
    'just bench',
    'just bench-x',
    'just doctest',
    'dotnet pack',
  ];
  const allowed = [
    'node scripts/human/job-queue.js run -- "dotnet test X"',
    'node scripts/human/job-queue.js run --gpu -- "just test-category Runtime"',
    'cd tmp/w && node scripts/human/job-queue.js run -- "dotnet build && dotnet test"',
    'node scripts/agent/build-queue.js run -- dotnet test X',
    'git commit -m "run dotnet test before merging"',
    'gh issue create --body "just test fails"',
    'dotnet build-server shutdown',
    'dotnet --info',
    'msg="fix: dotnet build failure"; git commit -m "$msg"',
    'dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle log.txt',
    'node scripts/human/gpu-lock.js --help',
    'node scripts/human/gpu-lock.js --self-test',
    'node --check scripts/human/gpu-lock.js',
    'node scripts/human/gpu-lock.js --lock-path tmp/lock -- "dotnet test X"',
    'node scripts/agent/gpu-lock.js --help',
    'dotnet test --help',
    'dotnet tool list',
    'dotnet tool restore',
    'dotnet tool install -g dotnet-counters',
    'dnx dotnet-counters',
    'node scripts/human/long-checks.js --dry-run',
    'just env-info',
    'just --list',
    'grep -n "dotnet test" justfile',
    'grep -E "foo|dotnet test" justfile',
    'rg "x; dotnet build" docs',
    'echo "a && dotnet test"',
    "cat > tmp/notes.md <<'EOF'\ndotnet build Henicos.slnx\njust test\nEOF",
    'cat > tmp/notes.md <<-EOF\n\tdotnet build x\n\tEOF',
    "$body = @'\ndotnet test\n'@\ngh issue comment 1 --body $body",
    '$msg = "fix; dotnet test now"; git commit -m $msg',
    'node -e "const a = 1;\nconsole.log(a)"',
    'node scripts/agent/check-issue-refs.js --text "x"',
    'git log --oneline | head -5',
    'ls 2>&1 | grep x',
    'rm -rf obj # intent: clean first; dotnet build checks it',
    'ls # && dotnet test',
    'cmd /c dir',
    "git commit -m \"$(cat <<'EOF'\nfix: run dotnet test (and just test) first\nEOF\n)\"",
    'echo "$(date) done"',
    'node scripts/human/test-memory.js --help',
    'node scripts/human/test-sanitized.js --help',
    'node scripts/human/mutate.js --dry-run',
    'node scripts/human/coverage-diff.js --help',
    'cuda-memcheck --help',
    'valgrind ls',
    'strace ls',
    'ltrace ls',
    'nsys --version',
    'ncu --help',
    'drmemory --help',
    'perf --version',
    'gdb --version',
    'lldb --version',
    'dotnet-trace ps',
    'dotnet-counters ps',
    'dotnet-coverage --help',
    'procdump -?',
    '@( echo ok )',
    'exec echo ok',
    'command -v dotnet',
    'if true; then echo ok; fi',
    'if false; then echo ok; elif true; then echo ok; fi',
    'if false; then echo ok; else echo done; fi',
    'while false; do echo ok; done',
    'until true; do echo ok; done',
    'just --dry-run coverage-diff',
    'just long-checks --dry-run',
    'just --show bench',
    'just --dry-run bench-x',
    'just --dry-run doctest',
    'dotnet pack --help',
  ];
  for (const command of denied) {
    assert.notDeepStrictEqual(check(command), [], `should deny: ${command}`);
  }
  for (const command of allowed) {
    assert.deepStrictEqual(check(command), [], `should allow: ${command}`);
  }
  // PowerShell (Claude Code PowerShell tool, Antigravity): ` escapes in double quotes, \ does not
  const deniedPowerShell = [
    'Set-Location "tmp\\"; dotnet test',
    'Remove-Item -Recurse -Force "obj\\"; dotnet build',
    'Write-Output "it\'s starting"; Write-Output "$(dotnet test)"',
    '$out = dotnet test',
    '$x=dotnet build',
    '$res = just test',
    '@(dotnet test)',
    '$r = @(dotnet test)',
    '$o = (dotnet test)',
    '$env:CI = "true"; dotnet test',
    '& "C:\\Program Files\\dotnet\\dotnet.exe" test',
    '$o = (just test)',
    '$o = & "C:\\Program Files\\dotnet\\dotnet.exe" test',
    'if ($x) { & "C:\\x\\dotnet.exe" test }',
    '(& "C:\\x\\dotnet.exe" test)',
    '$o = (& "C:\\x\\dotnet.exe" build)',
    'Get-ChildItem | ForEach-Object { & "C:\\x\\dotnet.exe" test $_ }',
    '. "C:\\x\\dotnet.exe" test',
    'git commit -m "$(dotnet test)"',
  ];
  const allowedPowerShell = [
    'Test-Path "src\\Compiler\\"; Write-Output "fix; dotnet test"',
    "gh pr comment 1 --body @'\nIt's fine. Run $(dotnet test) later.\n'@",
    'git status # see $(dotnet test)',
    'if ($true) { Write-Output "a `" { dotnet test `"" }',
    "Write-Output 'it''s $(dotnet test)'",
    '$x = 1; Get-ChildItem',
    '$msg="dotnet test"',
    '$tool = "msbuild"',
  ];
  for (const command of deniedPowerShell) {
    assert.notDeepStrictEqual(check(command, 'pwsh'), [], `should deny in PowerShell: ${command}`);
  }
  for (const command of allowedPowerShell) {
    assert.deepStrictEqual(check(command, 'pwsh'), [], `should allow in PowerShell: ${command}`);
  }
  assert.notDeepStrictEqual(check('echo "x\\"; dotnet test', 'cmd'), [], 'cmd: "x\\" ends the quote');
  assert.notDeepStrictEqual(check('call dotnet build', 'cmd'), [], 'cmd: call prefix');
  assert.deepStrictEqual(check('call echo ok', 'cmd'), [], 'cmd: call prefix with safe command');
  assert.notDeepStrictEqual(check('just profile=cpu fuzz-diff'), [], 'just variable before the recipe');
  checkGeneratedForms(assert);
  require('./self-test-rewrite.js').checkRewrites(assert);
  checkDetailedReasons(assert);
  checkReaderIsolation(assert);
  checkCustomProjectIsolation(assert);
  checkNoProjectImportInEngine(assert);
  checkHookFailsOpen(assert);
  process.stdout.write('check-queued-command self-test passed\n');
}

/**
 * Antigravity stops the command of a hook that exits with a non-zero code, so --hook must allow (no output, exit code
 * 0) and log when a module fails to load, the judgement throws or the input is not JSON; --text must still fail. Runs
 * copies of the entry in a temporary checkout whose modules are broken on purpose (the log lands in that checkout).
 */
function checkHookFailsOpen(assert) {
  const { spawnSync } = require('child_process');
  const real = (name) => JSON.stringify(path.join(__dirname, name));
  const working = {
    'project-henicos.js': `module.exports = require(${real('project-henicos.js')});`,
    'hook.js': `module.exports = require(${real('hook.js')});`,
    'command-rules.js': `module.exports = require(${real('command-rules.js')});`,
  };
  const payload = JSON.stringify({ toolCall: { name: 'run_command', args: { CommandLine: 'dotnet test X' } } });
  const cases = [
    {
      name: 'a module that fails to load',
      modules: { 'project-henicos.js': "throw new Error('broken project');" },
      logged: 'broken project',
      textFails: true,
    },
    {
      name: 'a judgement that throws',
      modules: {
        ...working,
        'command-rules.js':
          `const real = require(${real('command-rules.js')});\n` +
          'module.exports = { createRules: (project) => ({ ...real.createRules(project), check: () => {\n' +
          "  throw new Error('broken rule');\n} }) };",
      },
      logged: 'broken rule',
      textFails: true,
    },
    { name: 'input that is not JSON', modules: working, input: 'dotnet test X', logged: 'invalid hook input' },
    { name: 'an empty input', modules: working, input: '', logged: 'invalid hook input' },
    // The same copies answer a valid input as the hook does: the denial is printed in full and nothing is logged
    { name: 'a valid input', modules: working, logged: null, printed: '{"decision":"deny"' },
  ];
  for (const testCase of cases) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-fail-open-'));
    try {
      const entry = path.join(root, 'scripts', 'agent', 'check-queued-command.js');
      fs.mkdirSync(path.join(root, 'scripts', 'agent', 'check-queued-command'), { recursive: true });
      fs.copyFileSync(path.join(__dirname, '..', 'check-queued-command.js'), entry);
      for (const [file, text] of Object.entries(testCase.modules)) {
        fs.writeFileSync(path.join(root, 'scripts', 'agent', 'check-queued-command', file), `'use strict';\n${text}\n`);
      }
      const input = testCase.input === undefined ? payload : testCase.input;
      const hook = spawnSync(process.execPath, [entry, '--hook'], { input, encoding: 'utf8' });
      assert.strictEqual(hook.status, 0, `--hook with ${testCase.name} exits 0: ${hook.stderr}`);
      const logFile = path.join(root, 'tmp', 'hook-errors.log');
      if (testCase.logged === null) {
        assert.ok(hook.stdout.startsWith(testCase.printed) && hook.stdout.endsWith('}'), `--hook prints ${hook.stdout}`);
        assert.ok(!fs.existsSync(logFile), `--hook with ${testCase.name} logs nothing`);
        continue;
      }
      assert.strictEqual(hook.stdout, '', `--hook with ${testCase.name} prints nothing`);
      const log = fs.readFileSync(logFile, 'utf8');
      assert.ok(log.includes(testCase.logged), `--hook with ${testCase.name} logs it: ${log}`);
      if (testCase.textFails) {
        const text = spawnSync(process.execPath, [entry, '--text', 'dotnet test X'], { encoding: 'utf8' });
        assert.ok(text.status !== 0 && text.stderr.includes(testCase.logged), `--text with ${testCase.name} fails`);
      }
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }
}

/**
 * Every build / test command in every form that runs it must be denied, and in every form that only names it (quotes,
 * comments, heredocs, search patterns) allowed, per dialect. {B} stands for the command.
 */
function checkGeneratedForms(assert) {
  const builds = [
    'dotnet build',
    'dotnet test tests/X.csproj --filter "A|B"',
    'dotnet -d build -p:Foo="a b"',
    'msbuild Henicos.slnx',
    'just test',
    'just -f justfile test-category Runtime',
    'node --max-old-space-size=4096 scripts/human/test-stability.js',
  ];
  const runs = {
    bash: [
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
      'compute-sanitizer --tool memcheck {B}',
      '"$CS" --log-file "a b" {B}',
      'cat <<EOF\ntext\nEOF\n{B}',
      'echo a \\\n  && {B}',
      'git status && {B}',
    ],
    pwsh: [
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
  };
  const names = {
    bash: [
      'echo "{B}"',
      'git commit -m "run {B} first"',
      'git commit -m "$(cat <<\'EOF\'\nfix: run {B} first\nEOF\n)"',
      'grep -n "{B}" justfile',
      "cat > notes.md <<'EOF'\n{B}\nEOF",
      'ls # see; {B}',
      'echo "x\\"; {B}"',
      'msg="{B}"; echo "$msg"',
    ],
    pwsh: [
      'Write-Output "{B}"',
      "gh pr comment 1 --body @'\nIt's fine. Ran {B}.\n'@",
      'Select-String -Path justfile -Pattern "{B}"',
      'Get-ChildItem # see; {B}',
      '$msg = "{B}"; Write-Output $msg',
      'Test-Path "src\\x\\"; Write-Output "fix; {B}"',
    ],
  };
  // Inside double quotes the build's own double quotes become single ones, inside single quotes they are dropped
  const fill = (template, build) => {
    const before = template.slice(0, template.indexOf('{B}'));
    const single = (before.match(/'/g) || []).length % 2 === 1;
    const double = !single && (before.match(/"/g) || []).length % 2 === 1;
    const b = single ? build.replace(/'/g, '') : double ? build.replace(/"/g, "'") : build;
    return template.split('{B}').join(b);
  };
  for (const [kind, templates] of Object.entries(runs)) {
    for (const template of templates) {
      for (const build of builds) {
        const command = fill(template, build);
        assert.notDeepStrictEqual(check(command, kind), [], `should deny in ${kind}: ${command}`);
      }
    }
  }
  for (const [kind, templates] of Object.entries(names)) {
    for (const template of templates) {
      for (const build of builds) {
        const command = fill(template, build);
        assert.deepStrictEqual(check(command, kind), [], `should allow in ${kind}: ${command}`);
      }
    }
  }
}

/** Structured reason shapes (`{ tool, verb, text }`) and deduplication from `checkDetailed`. */
function checkDetailedReasons(assert) {
  const cases = [
    ['dotnet test tests/X.csproj', 'bash', [{ tool: 'dotnet', verb: 'test', text: 'dotnet test' }]],
    ['dotnet stryker', 'bash', [{ tool: 'dotnet', verb: 'stryker', text: 'dotnet stryker' }]],
    ['just test-category Runtime', 'bash', [{ tool: 'just', verb: 'test-category', text: 'just test-category' }]],
    [
      'node scripts/human/long-checks.js',
      'bash',
      [{ tool: 'node-script', verb: 'long-checks.js', text: 'long-checks.js' }],
    ],
    [
      './scripts/human/long-checks.js',
      'bash',
      [{ tool: 'node-script', verb: 'long-checks.js', text: 'long-checks.js' }],
    ],
    ['dotnet-stryker --since main', 'bash', [{ tool: 'stryker', verb: '', text: 'dotnet-stryker' }]],
    ['dotnet tool run dotnet-stryker', 'bash', [{ tool: 'stryker', verb: '', text: 'dotnet-stryker' }]],
    ['msbuild Henicos.slnx', 'bash', [{ tool: 'msbuild', verb: '', text: 'msbuild' }]],
    [
      'dotnet test A && echo $(dotnet test B) && dotnet build',
      'bash',
      [
        { tool: 'dotnet', verb: 'test', text: 'dotnet test' },
        { tool: 'dotnet', verb: 'build', text: 'dotnet build' },
      ],
    ],
    ['if ($x) { dotnet test }', 'pwsh', [{ tool: 'dotnet', verb: 'test', text: 'dotnet test' }]],
    ['wsl -- dotnet test', 'bash', [{ tool: 'dotnet', verb: 'test', text: 'dotnet test' }]],
    ['valgrind just test', 'bash', [{ tool: 'just', verb: 'test', text: 'just test' }]],
    ['echo ok', 'bash', []],
  ];
  for (const [command, kind, expected] of cases) {
    const detailed = checkDetailed(command, kind);
    assert.deepStrictEqual(detailed, expected, `checkDetailed(${kind}): ${command}`);
    assert.deepStrictEqual(
      detailed.map((reason) => reason.text),
      check(command, kind),
      `checkDetailed text parity(${kind}): ${command}`,
    );
  }
}

/** Independent `createReader(dialect)` instances do not share or mutate dialect state. */
function checkReaderIsolation(assert) {
  assert.strictEqual(shellReader.withDialect, undefined, 'withDialect removed');
  assert.strictEqual(shellReader.currentDialect, undefined, 'currentDialect removed');
  const bash = shellReader.createReader('bash');
  const pwsh = shellReader.createReader('pwsh');
  const cmd = shellReader.createReader('cmd');
  assert.strictEqual(shellReader.createReader().dialect, 'bash');
  assert.strictEqual(bash.dialect, 'bash');
  assert.strictEqual(pwsh.dialect, 'pwsh');
  assert.strictEqual(cmd.dialect, 'cmd');
  assert.deepStrictEqual(bash.splitSegments('echo "x\\" ; echo ok'), ['echo "x\\" ; echo ok']);
  assert.deepStrictEqual(pwsh.splitSegments('echo "x\\" ; echo ok'), ['echo "x\\" ', ' echo ok']);
  assert.deepStrictEqual(cmd.splitSegments('echo "x\\" ; echo ok'), ['echo "x\\" ', ' echo ok']);
  assert.deepStrictEqual(pwsh.splitSegments('echo "x`"; echo ok'), ['echo "x`"; echo ok']);
  assert.deepStrictEqual(bash.splitSegments('echo "x`"; echo ok'), ['echo "x`"', ' echo ok']);
  assert.deepStrictEqual(pwsh.substitutions('@(echo ok)'), ['echo ok']);
  assert.deepStrictEqual(bash.substitutions('@(echo ok)'), []);
  assert.deepStrictEqual(pwsh.words('&"echo" ok'), ['&', '"echo"', 'ok']);
  assert.deepStrictEqual(bash.words('&"echo" ok'), ['&"echo"', 'ok']);
  assert.strictEqual(pwsh.argumentText("'it''s'"), "it's");
  assert.strictEqual(bash.argumentText("'it''s'"), 'its');
  assert.deepStrictEqual(bash.splitSegments('echo "x\\" ; echo ok'), ['echo "x\\" ; echo ok']);
  assert.strictEqual(bash.dialect, 'bash');
  assert.strictEqual(pwsh.dialect, 'pwsh');
  assert.strictEqual(cmd.dialect, 'cmd');
}

/**
 * A project config with a key missing or of the wrong type stops the engine at creation (it never falls back to
 * allowing the commands that key would deny), naming the key.
 */
function checkProjectValidation(assert) {
  const { Schema } = require('./project-config.js');
  // The schema names every key of this repository's config, nested ones included, so that none goes unchecked
  const keys = (object, schema, where) =>
    Object.keys(object).flatMap((key) =>
      typeof schema[key] === 'object' ? keys(object[key], schema[key], `${where}${key}.`) : [`${where}${key}`],
    );
  assert.deepStrictEqual(keys(projectHenicos, Schema, '').sort(), keys(Schema, Schema, '').sort(), 'schema keys');
  const factories = [
    ['createRules', (project) => createRules(project)],
    ['createHook', (project) => createHook(project, createRules(projectHenicos))],
  ];
  for (const [label, create] of factories) {
    assert.throws(() => create(undefined), /must be an object/, `${label}: no config`);
    for (const [key, type] of Object.entries(Schema)) {
      const missing = { ...projectHenicos };
      delete missing[key];
      assert.throws(() => create(missing), new RegExp(`needs ${key} `), `${label}: ${key} missing`);
      const wrong = { ...projectHenicos, [key]: 7 };
      assert.throws(() => create(wrong), new RegExp(`needs ${key} `), `${label}: ${key} of the wrong type`);
      if (typeof type === 'object') {
        for (const inner of Object.keys(type)) {
          const nested = { ...projectHenicos, [key]: { ...projectHenicos[key] } };
          delete nested[key][inner];
          assert.throws(() => create(nested), new RegExp(`needs ${key}\\.${inner} `), `${label}: ${key}.${inner}`);
        }
      }
    }
  }
  const noNames = { ...projectHenicos, nodeScripts: { ...projectHenicos.nodeScripts, names: [] } };
  assert.throws(() => createRules(noNames), /needs nodeScripts\.names /, 'no script names');
  const emptyName = { ...projectHenicos, nodeScripts: { ...projectHenicos.nodeScripts, names: [''] } };
  assert.throws(() => createRules(emptyName), /needs nodeScripts\.names /, 'an empty script name');
  const dotted = { ...projectHenicos, nodeScripts: { ...projectHenicos.nodeScripts, names: ['my.tool'] } };
  const dotRules = createRules(dotted);
  assert.deepStrictEqual(dotRules.check('node tools/myXtool.js'), [], 'a dot in a script name is literal');
  assert.deepStrictEqual(dotRules.check('node tools/my.tool.js'), ['my.tool.js']);
}

/** A custom `project` config works in isolation with zero Henicos rule or wording leakage. */
function checkCustomProjectIsolation(assert) {
  checkProjectValidation(assert);
  const customProject = {
    justRecipes: /^(custom-task)$/,
    justDryRunRecipes: /^(custom-task)$/,
    nodeScripts: {
      names: ['custom-script'],
      noHelpExemption: /custom-script\.js$/,
      exemptOptions: [],
    },
    dotnet: {
      verbs: /^(customverb)$/,
      cpuVerbs: ['customverb'],
      testVerbs: [],
    },
    runners: ['custom-runner'],
    runnerTargets: ['dotnet', 'just', 'node'],
    exemptPrograms: ['svn'],
    queueScript: 'tools/custom-queue.js',
    queueScriptPattern: /(^|[\\/])custom-queue\.js$/,
    justfile: {
      fileName: 'justfile',
      queueLinePattern: /^queue := "node tools\/custom-queue\.js run"\s*$/m,
      optionsPattern: /^(--gpu( |$))*$/,
    },
    testProjects: {
      list: () => [],
      readErrorMessage: (root, error) => `CUSTOM READ: ${error.message}`,
      missingMessage: 'CUSTOM NO PROJECT',
      usesGpu: () => ({ gpu: false }),
    },
    messages: {
      denialText: (reasons) => `CUSTOM DENY: ${reasons.join('; ')}`,
      additionalContext: (plan) => `CUSTOM QUEUED: ${plan.command}`,
      undecidedNote: (undecided) => `CUSTOM UNDECIDED: ${undecided.join('; ')}`,
    },
  };
  const customRules = createRules(customProject);
  const customHook = createHook(customProject, customRules);
  assert.deepStrictEqual(customRules.check('just custom-task'), ['just custom-task']);
  assert.deepStrictEqual(customRules.check('just custom-task --dry-run'), []);
  assert.deepStrictEqual(customRules.check('node tools/custom-script.js --help'), ['custom-script.js']);
  assert.deepStrictEqual(customRules.check('dotnet customverb'), ['dotnet customverb']);
  assert.deepStrictEqual(customRules.check('custom-runner just custom-task'), ['just custom-task']);
  assert.deepStrictEqual(customRules.check('node tools/custom-queue.js run -- dotnet customverb'), []);
  assert.deepStrictEqual(customRules.check('svn commit -m "just custom-task"'), []);
  for (const henicosCmd of [
    'just test',
    'node scripts/human/long-checks.js',
    'dotnet test',
    'valgrind dotnet customverb',
    'valgrind just test',
  ]) {
    assert.deepStrictEqual(customRules.check(henicosCmd), [], `custom project ignores Henicos: ${henicosCmd}`);
  }
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'hook-custom-'));
  try {
    fs.mkdirSync(path.join(tmpDir, 'tools'), { recursive: true });
    fs.writeFileSync(path.join(tmpDir, 'tools', 'custom-queue.js'), '// stub\n');
    fs.writeFileSync(
      path.join(tmpDir, 'justfile'),
      'queue := "node tools/custom-queue.js run"\ncustom-task:\n  {{queue}} --gpu -- echo ok\n',
    );
    const rewritten = customHook.hookAnswer({
      tool_name: 'Bash',
      tool_input: { command: 'dotnet customverb' },
      cwd: tmpDir,
    }).hookSpecificOutput;
    assert.strictEqual(
      rewritten.updatedInput.command,
      "node tools/custom-queue.js run --shell bash -- 'dotnet customverb'",
    );
    assert.strictEqual(rewritten.additionalContext, `CUSTOM QUEUED: ${rewritten.updatedInput.command}`);
    const gpuRewritten = customHook.hookAnswer({
      tool_name: 'Bash',
      tool_input: { command: 'just custom-task' },
      cwd: tmpDir,
    }).hookSpecificOutput;
    assert.strictEqual(
      gpuRewritten.updatedInput.command,
      "node tools/custom-queue.js run --gpu --shell bash -- 'just custom-task'",
    );
    const agDeny = customHook.hookAnswer({
      toolCall: { name: 'run_command', args: { CommandLine: 'dotnet customverb' } },
    });
    assert.deepStrictEqual(agDeny, { decision: 'deny', reason: 'CUSTOM DENY: dotnet customverb' });
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

/** Engine modules never reference or load `project-henicos.js` or `target-dialect.js`. */
function checkNoProjectImportInEngine(assert) {
  const engineFiles = [
    'hook.js',
    'queue-rewrite.js',
    'command-rules.js',
    'shell-reader.js',
    'rules/wrappers.js',
    'rules/dotnet.js',
    'rules/just.js',
    'rules/node-scripts.js',
  ];
  for (const rel of engineFiles) {
    const filePath = path.join(__dirname, rel);
    const content = fs.readFileSync(filePath, 'utf8');
    assert.ok(
      !/project-henicos|job-queue-henicos|target-dialect/.test(content),
      `engine module must not reference project data: ${rel}`,
    );
    const resolved = require.resolve(filePath);
    require(resolved);
    const visited = new Set();
    const stack = [require.cache[resolved]];
    while (stack.length > 0) {
      const mod = stack.pop();
      if (!mod || visited.has(mod.filename)) {
        continue;
      }
      visited.add(mod.filename);
      assert.ok(
        !/project-henicos\.js$|target-dialect\.js$/.test(mod.filename),
        `${rel} loaded forbidden dependency: ${mod.filename}`,
      );
      for (const child of mod.children || []) {
        stack.push(child);
      }
    }
  }
}

module.exports = { selfTest };
