'use strict';

// The self-test of check-queued-command.js (--self-test): neutral engine checks (dialects, quoting, substitutions,
// wrappers, engine default runners, rule order, tolerant/strict error modes, isolation, fast allow-path, fail-open),
// strict loading of the active job-queue.config.json, and execution of every configured plugin's hook-checks.js.

const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const shellReader = require('./shell-reader.js');
const {
  PluginsRoot,
  loadHookConfig,
  resolvePluginFolder,
  validateConfigSchema,
  validateHookIo,
  validateHookRules,
} = require('./plugin-loader.js');
const { loadConfigFile } = require('../../human/job-queue/lib/config-file.js');

/** Writes a neutral fake `buildtool` plugin, optional `hook-io.js`, and `job-queue.config.json` into a temp dir. */
function createNeutralWorkspace({
  entry = 'tools/custom-queue.js',
  scriptNames = ['build-script', 'lock-tool'],
  extraPlugins = [],
  includeDefaultIo = true,
  runners = ['custom-runner'],
} = {}) {
  const root = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-engine-')));
  const pluginDir = path.join(root, 'buildtool-plugin');
  fs.mkdirSync(pluginDir, { recursive: true });
  fs.writeFileSync(
    path.join(pluginDir, 'hook-rules.js'),
    [
      "'use strict';",
      'module.exports = {',
      "  name: './buildtool-plugin',",
      `  runners: ${JSON.stringify(runners)},`,
      "  runnerTargets: ['buildtool'],",
      "  legacyQueueScripts: ['old-queue.js'],",
      '  nodeScripts: {',
      `    names: ${JSON.stringify(scriptNames)},`,
      '    noHelpExemption: /build-script\\.js$/,',
      "    exemptOptions: [{ script: /lock-tool\\.js$/, option: '--lock-path' }],",
      '  },',
      '  rules() {',
      '    return [{',
      "      matches: ({ name }) => name === 'buildtool',",
      '      reason: (_reader, { args }) => {',
      "        const idx = args.findIndex((a) => !a.startsWith('-'));",
      "        const verb = idx < 0 ? '' : args[idx];",
      "        const rest = args.slice(idx + 1);",
      "        if (rest.some((a) => ['--help', '-h', '-?', '/?'].includes(a))) return null;",
      "        if (['build', 'test', 'publish', 'pack', 'watch'].includes(verb)) {",
      "          return { tool: 'buildtool', verb, text: 'buildtool ' + verb };",
      '        }',
      '        return null;',
      '      },',
      '    }];',
      '  },',
      '};',
    ].join('\n'),
  );

  const plugins = [{ name: './buildtool-plugin' }, ...extraPlugins];
  if (includeDefaultIo) {
    const ioDir = path.join(root, 'io-plugin');
    fs.mkdirSync(ioDir, { recursive: true });
    fs.writeFileSync(
      path.join(ioDir, 'hook-io.js'),
      [
        "'use strict';",
        'module.exports = {',
        "  name: './io-plugin',",
        '  read(payload) {',
        "    if (!payload || typeof payload !== 'object') return null;",
        '    if (payload.toolCall) {',
        '      const cmd = (payload.toolCall.args && payload.toolCall.args.CommandLine) || "";',
        "      return { command: String(cmd), kind: 'pwsh', canRewrite: false };",
        '    }',
        '    if (payload.tool_input || payload.tool_name) {',
        '      const ti = payload.tool_input || {};',
        "      const kind = payload.tool_name === 'PowerShell' ? 'pwsh' : 'bash';",
        "      return { command: String(ti.command || ''), kind, cwd: payload.cwd, canRewrite: true, toolInput: ti };",
        '    }',
        '    return null;',
        '  },',
        "  deny(_read, reason) { return { decision: 'deny', reason }; },",
        '  rewrite(read, command, additionalContext) {',
        '    return read.canRewrite ? { updatedInput: { ...read.toolInput, command }, additionalContext } : null;',
        '  },',
        '};',
      ].join('\n'),
    );
    plugins.push({ name: './io-plugin' });
  }

  const configPath = path.join(root, 'job-queue.config.json');
  fs.writeFileSync(configPath, JSON.stringify({ version: 1, entry, plugins }));
  return { root, configPath: fs.realpathSync.native(configPath) };
}

function selfTest(createEngine) {
  const assert = require('node:assert');
  const prevConfig = process.env.JOB_QUEUE_CONFIG;
  const ws = createNeutralWorkspace();
  try {
    const { check, checkDetailed } = createEngine({ configPath: ws.configPath, tolerant: false });

    const denied = [
      'buildtool test tests/Unit.proj',
      'cd tmp/w && buildtool build Solution.slnx -nodeReuse:false',
      'DiffEngine_Disabled=true buildtool test',
      'node tools/lock-tool.js -- "buildtool test X"',
      'node tools/build-script.js --count 3',
      'node tools/build-script.js -- --dry-run',
      'node tools/build-script.js --help',
      '& buildtool test',
      '"C:\\Program Files\\BuildTool\\buildtool.exe" build',
      'git status && buildtool test',
      'echo custom-queue.js; buildtool test',
      'timeout 600 buildtool test',
      'nohup buildtool build &',
      '(buildtool build)',
      'cmd /c "buildtool test x"',
      'pwsh -c "buildtool build"',
      'bash -c "cd x && buildtool test"',
      'Start-Process buildtool -ArgumentList "test x" -Wait',
      'buildtool watch test',
      'buildtool -p:Foo="a b" test',
      'FOO="a b" buildtool test',
      'nice -n 10 buildtool build',
      'stdbuf -oL buildtool test',
      'env -C tmp buildtool test',
      'do { buildtool test } while ($false)',
      'buildtool publish -c Release',
      "cat > tmp/a.md <<'EOF'\nnot a command\nEOF\nbuildtool test",
      'Set-Location x; buildtool test',
      'buildtool \\\n  test --filter X',
      'buildtool `\r\n  build x',
      'powershell -NoProfile -Command "buildtool test"',
      'pwsh -NoProfile -c "buildtool build"',
      'bash -lc "buildtool test"',
      'cmd.exe /d /c "buildtool test"',
      'if true; then buildtool test; fi',
      'for p in a b; do buildtool build $p; done',
      'if ($x) { buildtool test }',
      'foreach ($p in $list) { buildtool build $p }',
      'echo $(buildtool test)',
      'x=$(buildtool build)',
      'node --max-old-space-size=4096 tools/build-script.js',
      './tools/build-script.js',
      'Start-Process buildtool "test x"',
      'buildtool -d build',
      'sudo buildtool test',
      'ls | xargs -n1 buildtool build',
      'Invoke-Expression "buildtool test"',
      '! buildtool build',
      'custom-runner buildtool test',
      'valgrind buildtool test',
      'strace buildtool build',
      'ltrace buildtool build',
      'drmemory -- buildtool test',
      'perf record buildtool test',
      'gdb --args buildtool test',
      'lldb -- buildtool test',
      'procdump -x . buildtool test',
      '@( buildtool test )',
      'exec buildtool test',
      'command buildtool build',
      'if buildtool test; then echo ok; fi',
      'if false; then echo ok; elif buildtool test; then echo ok; fi',
      'if false; then echo ok; else buildtool test; fi',
      'while buildtool test; do echo ok; done',
      'until buildtool test; do echo ok; done',
      'buildtool pack',
      '"$X/buildtool.exe" test',
      '$BT/buildtool test',
    ];
    const allowed = [
      'node tools/custom-queue.js run -- "buildtool test X"',
      'cd tmp/w && node tools/custom-queue.js run -- "buildtool build && buildtool test"',
      'node tools/old-queue.js run -- buildtool test X',
      'git commit -m "run buildtool test before merging"',
      'gh issue create --body "buildtool test fails"',
      'buildtool --info',
      'msg="fix: buildtool build failure"; git commit -m "$msg"',
      'buildtool run -- tool log.txt',
      'node tools/lock-tool.js --help',
      'node tools/lock-tool.js --self-test',
      'node --check tools/lock-tool.js',
      'node tools/lock-tool.js --lock-path tmp/lock -- "buildtool test X"',
      'buildtool test --help',
      'grep -n "buildtool test" Makefile',
      'grep -E "foo|buildtool test" Makefile',
      'rg "x; buildtool build" docs',
      'echo "a && buildtool test"',
      "cat > tmp/notes.md <<'EOF'\nbuildtool build\nEOF",
      'cat > tmp/notes.md <<-EOF\n\tbuildtool build x\n\tEOF',
      "$body = @'\nbuildtool test\n'@\ngh issue comment 1 --body $body",
      '$msg = "fix; buildtool test now"; git commit -m $msg',
      'node -e "const a = 1;\nconsole.log(a)"',
      'node scripts/agent/check-issue-refs.js --text "x"',
      'git log --oneline | head -5',
      'ls 2>&1 | grep x',
      'rm -rf obj # intent: clean first; buildtool build checks it',
      'ls # && buildtool test',
      'cmd /c dir',
      "git commit -m \"$(cat <<'EOF'\nfix: run buildtool test first\nEOF\n)\"",
      'echo "$(date) done"',
      'valgrind ls',
      'strace ls',
      'ltrace ls',
      'drmemory --help',
      'perf --version',
      'gdb --version',
      'lldb --version',
      'procdump -?',
      '@( echo ok )',
      'exec echo ok',
      'command -v buildtool',
      'if true; then echo ok; fi',
      'if false; then echo ok; elif true; then echo ok; fi',
      'if false; then echo ok; else echo done; fi',
      'while false; do echo ok; done',
      'until true; do echo ok; done',
      'buildtool pack --help',
      '"$BUILDTOOL" test',
      '"$S/build-script.js"',
      '"$X/node" tools/build-script.js',
    ];
    for (const command of denied) {
      assert.notDeepStrictEqual(check(command), [], `should deny: ${command}`);
    }
    for (const command of allowed) {
      assert.deepStrictEqual(check(command), [], `should allow: ${command}`);
    }

    const deniedPowerShell = [
      'Set-Location "tmp\\"; buildtool test',
      'Remove-Item -Recurse -Force "obj\\"; buildtool build',
      'Write-Output "it\'s starting"; Write-Output "$(buildtool test)"',
      '$out = buildtool test',
      '$x=buildtool build',
      '@(buildtool test)',
      '$r = @(buildtool test)',
      '$o = (buildtool test)',
      '$env:CI = "true"; buildtool test',
      '& "C:\\Program Files\\BuildTool\\buildtool.exe" test',
      '$o = & "C:\\Program Files\\BuildTool\\buildtool.exe" test',
      'if ($x) { & "C:\\x\\buildtool.exe" test }',
      '(& "C:\\x\\buildtool.exe" test)',
      '$o = (& "C:\\x\\buildtool.exe" build)',
      'Get-ChildItem | ForEach-Object { & "C:\\x\\buildtool.exe" test $_ }',
      '. "C:\\x\\buildtool.exe" test',
      'git commit -m "$(buildtool test)"',
    ];
    const allowedPowerShell = [
      'Test-Path "src\\"; Write-Output "fix; buildtool test"',
      "gh pr comment 1 --body @'\nIt's fine. Run $(buildtool test) later.\n'@",
      'git status # see $(buildtool test)',
      'if ($true) { Write-Output "a `" { buildtool test `"" }',
      "Write-Output 'it''s $(buildtool test)'",
      '$x = 1; Get-ChildItem',
      '$msg="buildtool test"',
      '$tool = "buildtool"',
    ];
    for (const command of deniedPowerShell) {
      assert.notDeepStrictEqual(check(command, 'pwsh'), [], `should deny in PowerShell: ${command}`);
    }
    for (const command of allowedPowerShell) {
      assert.deepStrictEqual(check(command, 'pwsh'), [], `should allow in PowerShell: ${command}`);
    }
    assert.notDeepStrictEqual(check('echo "x\\"; buildtool test', 'cmd'), [], 'cmd: "x\\" ends the quote');
    assert.notDeepStrictEqual(check('call buildtool build', 'cmd'), [], 'cmd: call prefix');
    assert.deepStrictEqual(check('call echo ok', 'cmd'), [], 'cmd: call prefix with safe command');

    checkGeneratedForms(assert, check);
    checkDetailedReasons(assert, check, checkDetailed);
  } finally {
    fs.rmSync(ws.root, { recursive: true, force: true });
    if (prevConfig === undefined) {
      delete process.env.JOB_QUEUE_CONFIG;
    } else {
      process.env.JOB_QUEUE_CONFIG = prevConfig;
    }
  }

  require('./self-test-rewrite.js').checkRewrites(assert, createEngine);
  checkReaderIsolation(assert);
  checkPluginValidationAndIsolation(assert, createEngine);
  checkZeroPluginEngine(assert, createEngine);
  checkTolerantAndStrictModes(assert, createEngine);
  checkPluginFailuresKeepDenials(assert, createEngine);
  checkFastAllowPath(assert);
  checkNoProjectImportInEngine(assert, createEngine);
  checkBuiltInPluginIsolation(assert);
  checkHookFailsOpen(assert);
  runActiveConfigChecks(assert, createEngine);
  process.stdout.write('check-queued-command self-test passed\n');
}

/**
 * Every build / test command in every form that runs it must be denied, and in every form that only names it (quotes,
 * comments, heredocs, search patterns) allowed, per dialect. {B} stands for the command.
 */
function checkGeneratedForms(assert, check) {
  const builds = [
    'buildtool build',
    'buildtool test tests/Unit --filter "A|B"',
    'buildtool -d build -p:Foo="a b"',
    'node --max-old-space-size=4096 tools/build-script.js',
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
      'custom-runner --tool memcheck {B}',
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
      'grep -n "{B}" Makefile',
      "cat > notes.md <<'EOF'\n{B}\nEOF",
      'ls # see; {B}',
      'echo "x\\"; {B}"',
      'msg="{B}"; echo "$msg"',
    ],
    pwsh: [
      'Write-Output "{B}"',
      "gh pr comment 1 --body @'\nIt's fine. Ran {B}.\n'@",
      'Select-String -Path Makefile -Pattern "{B}"',
      'Get-ChildItem # see; {B}',
      '$msg = "{B}"; Write-Output $msg',
      'Test-Path "src\\x\\"; Write-Output "fix; {B}"',
    ],
  };
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

function checkDetailedReasons(assert, check, checkDetailed) {
  const cases = [
    ['buildtool test tests/X', 'bash', [{ tool: 'buildtool', verb: 'test', text: 'buildtool test' }]],
    ['node tools/build-script.js', 'bash', [{ tool: 'node-script', verb: 'build-script.js', text: 'build-script.js' }]],
    ['./tools/build-script.js', 'bash', [{ tool: 'node-script', verb: 'build-script.js', text: 'build-script.js' }]],
    [
      'buildtool test A && echo $(buildtool test B) && buildtool build',
      'bash',
      [
        { tool: 'buildtool', verb: 'test', text: 'buildtool test' },
        { tool: 'buildtool', verb: 'build', text: 'buildtool build' },
      ],
    ],
    ['if ($x) { buildtool test }', 'pwsh', [{ tool: 'buildtool', verb: 'test', text: 'buildtool test' }]],
    ['wsl -- buildtool test', 'bash', [{ tool: 'buildtool', verb: 'test', text: 'buildtool test' }]],
    ['valgrind buildtool test', 'bash', [{ tool: 'buildtool', verb: 'test', text: 'buildtool test' }]],
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

function checkPluginValidationAndIsolation(assert, createEngine) {
  assert.throws(() => validateConfigSchema(null), /config root/);
  assert.throws(() => validateConfigSchema({ version: 2, entry: 'q.js', plugins: [] }), /version/);
  assert.throws(() => validateConfigSchema({ version: 1, entry: '', plugins: [] }), /entry/);
  assert.throws(() => validateConfigSchema({ version: 1, entry: 'q.js', plugins: 'no' }), /plugins/);
  assert.throws(() => validateConfigSchema({ version: 1, entry: 'q.js', plugins: [{}] }), /plugins\[0\]\.name/);
  assert.throws(
    () => validateConfigSchema({ version: 1, entry: 'q.js', plugins: [{ name: 'a', options: [] }] }),
    /plugins\[0\]\.options/,
  );

  assert.throws(() => validateHookRules(null, 'x'), /must export an object/);
  assert.throws(() => validateHookRules({ name: 'y' }, 'x'), /name must be "x"/);
  assert.throws(() => validateHookRules({ name: 'x', runners: [''] }, 'x'), /runners/);
  assert.throws(() => validateHookRules({ name: 'x', messages: { denialText: 1 } }, 'x'), /messages\.denialText/);
  assert.throws(
    () => validateHookRules({ name: 'x', nodeScripts: { names: [], noHelpExemption: /a/, exemptOptions: [] } }, 'x'),
    /nodeScripts\.names/,
  );

  assert.throws(() => validateHookIo({ name: 'x', read: () => null }, 'x'), /needs deny/);
  assert.throws(() => validateHookIo({ name: 'x', deny: () => ({}) }, 'x'), /needs read/);

  const root = fs.realpathSync.native(os.tmpdir());
  assert.strictEqual(resolvePluginFolder('../escape', root), null);
  assert.strictEqual(resolvePluginFolder('a/b', root), null);
  assert.strictEqual(resolvePluginFolder('a\\b', root), null);
  assert.strictEqual(resolvePluginFolder('./local', root), path.resolve(root, './local'));

  // Literal dot in script name + two isolated instances without shared state
  const wsDot = createNeutralWorkspace({ entry: 'tools/dot-queue.js', scriptNames: ['my.tool'] });
  const wsAlt = createNeutralWorkspace({ entry: 'tools/alt-queue.js', scriptNames: ['other-tool'] });
  try {
    const dotEngine = createEngine({ configPath: wsDot.configPath, tolerant: false });
    const altEngine = createEngine({ configPath: wsAlt.configPath, tolerant: false });
    assert.deepStrictEqual(dotEngine.check('node tools/myXtool.js'), [], 'dot in script name is literal');
    assert.deepStrictEqual(dotEngine.check('node tools/my.tool.js'), ['my.tool.js']);
    assert.deepStrictEqual(dotEngine.check('node tools/other-tool.js'), [], 'isolated instances do not leak');
    assert.deepStrictEqual(altEngine.check('node tools/my.tool.js'), []);
    assert.deepStrictEqual(altEngine.check('node tools/other-tool.js'), ['other-tool.js']);
    assert.ok(dotEngine.denialText(['x']).includes('node tools/dot-queue.js run'));
    assert.ok(altEngine.denialText(['x']).includes('node tools/alt-queue.js run'));
  } finally {
    fs.rmSync(wsDot.root, { recursive: true, force: true });
    fs.rmSync(wsAlt.root, { recursive: true, force: true });
  }
}

function checkZeroPluginEngine(assert, createEngine) {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-zero-'));
  try {
    const configPath = path.join(tmpDir, 'job-queue.config.json');
    fs.writeFileSync(configPath, JSON.stringify({ version: 1, entry: 'tools/q.js', plugins: [] }));
    const zero = createEngine({ configPath: fs.realpathSync.native(configPath), tolerant: false });
    const samples = [
      'buildtool test X',
      'valgrind buildtool',
      'node tools/build-script.js',
      ['dot' + 'net', 'test', 'X'].join(' '),
      ['ju' + 'st', 'test'].join(' '),
      'ms' + 'build',
    ];
    for (const cmd of samples) {
      assert.deepStrictEqual(zero.check(cmd), [], `0-plugin engine allows: ${cmd}`);
    }
    assert.strictEqual(
      zero.hookAnswer({ tool_name: 'Bash', tool_input: { command: 'buildtool test' }, cwd: tmpDir }),
      null,
      '0-plugin engine hookAnswer returns null',
    );
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

function checkTolerantAndStrictModes(assert, createEngine) {
  const tmpRoot = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-modes-')));
  try {
    const entry = path.join(tmpRoot, 'scripts', 'agent', 'check-queued-command.js');
    const engineDir = path.join(tmpRoot, 'scripts', 'agent', 'check-queued-command');
    const libDir = path.join(tmpRoot, 'scripts', 'human', 'job-queue', 'lib');
    fs.mkdirSync(path.join(engineDir, 'rules'), { recursive: true });
    fs.mkdirSync(libDir, { recursive: true });
    fs.copyFileSync(path.join(__dirname, '..', 'check-queued-command.js'), entry);
    for (const f of ['plugin-loader.js', 'command-rules.js', 'queue-rewrite.js', 'hook.js', 'shell-reader.js']) {
      fs.copyFileSync(path.join(__dirname, f), path.join(engineDir, f));
    }
    for (const f of ['wrappers.js', 'node-scripts.js']) {
      fs.copyFileSync(path.join(__dirname, 'rules', f), path.join(engineDir, 'rules', f));
    }
    fs.copyFileSync(
      path.join(__dirname, '..', '..', 'human', 'job-queue', 'lib', 'config-file.js'),
      path.join(libDir, 'config-file.js'),
    );

    const goodDir = path.join(tmpRoot, 'good-plugin');
    const badDir = path.join(tmpRoot, 'bad-plugin');
    const ioDir = path.join(tmpRoot, 'io-plugin');
    fs.mkdirSync(goodDir, { recursive: true });
    fs.mkdirSync(badDir, { recursive: true });
    fs.mkdirSync(ioDir, { recursive: true });
    fs.writeFileSync(
      path.join(goodDir, 'hook-rules.js'),
      [
        "'use strict';",
        'module.exports = {',
        "  name: './good-plugin',",
        "  rules: () => [{ matches: ({ name }) => name === 'goodtool',",
        "    reason: () => ({ tool: 'good', verb: '', text: 'goodtool' }) }],",
        '};',
      ].join('\n'),
    );
    fs.writeFileSync(path.join(badDir, 'hook-rules.js'), "'use strict'; throw new Error('broken plugin load');");
    fs.writeFileSync(
      path.join(ioDir, 'hook-io.js'),
      [
        "'use strict';",
        'module.exports = {',
        "  name: './io-plugin',",
        "  read: (p) => (p && p.cmd ? { command: p.cmd, kind: 'bash', canRewrite: false } : null),",
        "  deny: (_r, reason) => ({ decision: 'deny', reason }),",
        '};',
      ].join('\n'),
    );

    const cfgPath = path.join(tmpRoot, 'job-queue.config.json');
    fs.writeFileSync(
      cfgPath,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [{ name: './bad-plugin' }, { name: './good-plugin' }, { name: './io-plugin' }],
      }),
    );

    // 1. Throwing plugin: tolerant --hook skips only the bad plugin and logs 1 line; strict --text fails
    const hookGood = spawnSync(process.execPath, [entry, '--hook'], {
      input: JSON.stringify({ cmd: 'goodtool' }),
      encoding: 'utf8',
    });
    assert.strictEqual(hookGood.status, 0, `--hook exits 0: ${hookGood.stderr}`);
    assert.ok(hookGood.stdout.includes('deny') && hookGood.stdout.includes('goodtool'), 'valid plugin still denies');
    const logLines1 = fs.readFileSync(path.join(tmpRoot, 'tmp', 'hook-errors.log'), 'utf8').trim().split('\n');
    assert.strictEqual(logLines1.length, 1, 'logs 1 error line for bad plugin');
    assert.ok(logLines1[0].includes('broken plugin load'));

    const textFail = spawnSync(process.execPath, [entry, '--text', 'goodtool'], { encoding: 'utf8' });
    assert.ok(textFail.status !== 0 && textFail.stderr.includes('broken plugin load'), 'strict --text fails');
    assert.ok(textFail.stderr.includes('plugin ./bad-plugin hook-rules.js: broken plugin load'), 'names the plugin');

    // 2. Non-JSON config: tolerant --hook exits 0 with empty stdout and logs 1 line
    fs.writeFileSync(cfgPath, '{not valid json');
    const hookBadJson = spawnSync(process.execPath, [entry, '--hook'], {
      input: JSON.stringify({ cmd: 'goodtool' }),
      encoding: 'utf8',
    });
    assert.strictEqual(hookBadJson.status, 0);
    assert.strictEqual(hookBadJson.stdout, '', 'non-JSON config allows all in --hook');
    const logLines2 = fs.readFileSync(path.join(tmpRoot, 'tmp', 'hook-errors.log'), 'utf8').trim().split('\n');
    assert.strictEqual(logLines2.length, 2, 'logs 1 additional line for invalid JSON');

    // 3. Missing config: tolerant --hook exits 0 with empty stdout; strict throws
    fs.rmSync(cfgPath);
    const hookMissing = spawnSync(process.execPath, [entry, '--hook'], {
      input: JSON.stringify({ cmd: 'goodtool' }),
      encoding: 'utf8',
    });
    assert.strictEqual(hookMissing.status, 0);
    assert.strictEqual(hookMissing.stdout, '');
    assert.throws(() => loadHookConfig({ configPath: cfgPath, tolerant: false }), /not found/);

    // 4. Runtime rule throw in tolerant vs strict mode
    const throwPluginDir = path.join(tmpRoot, 'throw-rule');
    fs.mkdirSync(throwPluginDir, { recursive: true });
    fs.writeFileSync(
      path.join(throwPluginDir, 'hook-rules.js'),
      [
        "'use strict';",
        'module.exports = {',
        "  name: './throw-rule',",
        "  rules: () => [{ matches: () => { throw new Error('boom match'); }, reason: () => null }],",
        '};',
      ].join('\n'),
    );
    fs.writeFileSync(
      cfgPath,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [{ name: './throw-rule' }, { name: './good-plugin' }],
      }),
    );
    const errors = [];
    const tolerantEng = createEngine({ configPath: cfgPath, tolerant: true, onError: (e) => errors.push(e) });
    assert.deepStrictEqual(tolerantEng.check('goodtool'), ['goodtool']);
    assert.ok(errors.some((e) => e.message.includes('boom match')), 'tolerant catches runtime rule throw');
    const strictEng = createEngine({ configPath: cfgPath, tolerant: false });
    assert.throws(
      () => strictEng.check('goodtool'),
      /plugin \.\/throw-rule matches\(\): boom match/,
      'strict throws on runtime rule throw, naming the plugin',
    );

    // 5. --hook records a failure once per command, not once per segment and pass
    fs.writeFileSync(
      cfgPath,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [{ name: './throw-rule' }, { name: './good-plugin' }, { name: './io-plugin' }],
      }),
    );
    const logFile = path.join(tmpRoot, 'tmp', 'hook-errors.log');
    const linesBefore = fs.readFileSync(logFile, 'utf8').trim().split('\n').length;
    const hookMany = spawnSync(process.execPath, [entry, '--hook'], {
      input: JSON.stringify({ cmd: 'cd a && goodtool && echo 1 && echo 2 && echo 3' }),
      encoding: 'utf8',
    });
    assert.ok(hookMany.status === 0 && hookMany.stdout.includes('goodtool'), `still denies: ${hookMany.stderr}`);
    const linesAfter = fs.readFileSync(logFile, 'utf8').trim().split('\n').length;
    assert.strictEqual(linesAfter - linesBefore, 1, 'one log line for a rule that fails on every segment');
  } finally {
    fs.rmSync(tmpRoot, { recursive: true, force: true });
  }
}

/**
 * A broken wording function or agent plugin never turns a denial into an allowed command: the default wording is
 * used, a failed rewrite falls back to the denial, and a failed deny() leaves the answer to the next agent plugin.
 * Strict mode names the plugin and the part that failed.
 */
function checkPluginFailuresKeepDenials(assert, createEngine) {
  const { root, configPath } = createNeutralWorkspace({ entry: 'queue.js', includeDefaultIo: false });
  try {
    fs.writeFileSync(path.join(root, 'queue.js'), '// stub\n');
    const write = (dir, lines) => {
      fs.mkdirSync(path.join(root, dir), { recursive: true });
      fs.writeFileSync(path.join(root, dir, dir.startsWith('io') ? 'hook-io.js' : 'hook-rules.js'), lines.join('\n'));
    };
    write('words', [
      "'use strict';",
      'module.exports = {',
      "  name: './words',",
      '  rewrite: (_s, list) => (list[0] === \'buildtool\' ? { gpu: false, options: [] } : null),',
      '  messages: {',
      "    denialText: () => { throw new Error('broken wording'); },",
      "    additionalContext: () => 'context',",
      "    undecidedNote: () => 'note',",
      '  },',
      '};',
    ]);
    const io = (name, deny, rewrite) => [
      "'use strict';",
      'module.exports = {',
      `  name: './${name}',`,
      "  read: (p) => ({ command: p.cmd, kind: 'bash', cwd: p.cwd, canRewrite: true }),",
      `  deny: ${deny},`,
      `  rewrite: ${rewrite},`,
      '};',
    ];
    write('io-broken', io('io-broken', "() => { throw new Error('broken deny'); }", "() => { throw new Error('x'); }"));
    write('io-plain', io('io-plain', "(_r, reason) => ({ decision: 'deny', reason })", '() => null'));
    const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
    config.plugins.push({ name: './words' }, { name: './io-broken' }, { name: './io-plain' });
    fs.writeFileSync(configPath, JSON.stringify(config));

    const errors = [];
    const tolerant = createEngine({ configPath, tolerant: true, onError: (e) => errors.push(e.message) });
    const expected = { decision: 'deny', reason: tolerant.denialText(['buildtool test']) };
    assert.ok(expected.reason.startsWith('buildtool test must run through the job queue'), 'default wording');
    // io-broken: rewrite() throws, so the command is denied; its deny() throws too, so io-plain answers
    assert.deepStrictEqual(tolerant.hookAnswer({ cmd: 'buildtool test', cwd: root }), expected);
    for (const part of ['messages.denialText()', 'hook-io.js rewrite()', 'hook-io.js deny()']) {
      assert.ok(errors.some((message) => message.includes(part)), `records the failure of ${part}: ${errors}`);
    }
    const strict = createEngine({ configPath, tolerant: false });
    assert.throws(() => strict.denialText(['buildtool test']), /plugin \.\/words messages\.denialText\(\)/);
    assert.throws(
      () => strict.hookAnswer({ cmd: 'buildtool test', cwd: root }),
      /plugin \.\/(words|io-broken) /,
      'strict names the plugin',
    );
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

/** Fast allow-path invariant: zero `.ts` modules loaded and zero child_process calls. */
function checkFastAllowPath(assert) {
  const entryPath = path.resolve(__dirname, '..', 'check-queued-command.js');
  const script = [
    "const cp = require('node:child_process');",
    "const fail = () => { throw new Error('no subprocess on allow path'); };",
    "cp.spawn = fail; cp.spawnSync = fail; cp.execFileSync = fail; cp.execSync = fail;",
    `const mod = require(${JSON.stringify(entryPath)});`,
    "const assert = require('node:assert');",
    "assert.deepStrictEqual(mod.check('ls -la'), []);",
    "const allowPayload = { tool_name: 'Bash', tool_input: { command: 'ls -la' }, cwd: process.cwd() };",
    'assert.strictEqual(mod.hookAnswer(allowPayload), null);',
    "const tsFiles = Object.keys(require.cache).filter((p) => p.endsWith('.ts'));",
    "assert.deepStrictEqual(tsFiles, [], 'no .ts in require.cache: ' + tsFiles.join(', '));",
  ].join('\n');
  const res = spawnSync(process.execPath, ['-e', script], { encoding: 'utf8' });
  assert.strictEqual(res.status, 0, `fast allow path failed: ${res.stderr}`);
}

/** Engine modules never reference or load repository or plugin names. */
function checkNoProjectImportInEngine(assert, createEngine) {
  const engineFiles = [
    '../check-queued-command.js',
    'plugin-loader.js',
    'command-rules.js',
    'queue-rewrite.js',
    'hook.js',
    'shell-reader.js',
    'rules/wrappers.js',
    'rules/node-scripts.js',
    'self-test.js',
    'self-test-rewrite.js',
  ];
  const ruleDirFiles = fs.readdirSync(path.join(__dirname, 'rules')).sort();
  assert.deepStrictEqual(ruleDirFiles, ['node-scripts.js', 'wrappers.js'], 'rules/ holds only generic helpers');

  // Build forbidden pattern dynamically so self-test.js itself contains no literal banned identifiers
  const bannedWords = [
    'hen' + 'icos',
    'gsh' + 'arp',
    'kas' + 'syi',
    'dot' + 'net',
    'msb' + 'uild',
    'stry' + 'ker',
    'ju' + 'stfile',
    'cu' + 'da',
    'nvi' + 'dia',
    'ns' + 'ys',
    'n' + 'cu',
    'cla' + 'ude',
    'anti' + 'gravity',
    'gpu-' + 'lock',
    'test-' + 'stability',
    'long-' + 'checks',
    'fuzz-' + 'differential',
    'build-' + 'queue',
  ];
  const bannedPaths = [
    'project-' + 'hen' + 'icos',
    'job-queue-' + 'hen' + 'icos',
    'target-' + 'dialect',
    'project-' + 'config',
    'compute-' + 'sanitizer',
    'cu' + 'da-memcheck',
    'dot' + 'net-stry' + 'ker',
    'plugins[\\\\/](' + ['dot' + 'net', 'ju' + 'st', 'nvi' + 'dia-gpu', 'ag' + 'ents'].join('|') + ')',
    '\\b' + 'ju' + 'st\\s+(test|recipe|build|profile|coverage|mutate|long-' + 'checks|bench|doctest)\\b',
    '[\'"`]' + 'ju' + 'st[\'"`]',
  ];
  const forbiddenEngineRef = new RegExp(`\\b(${bannedWords.join('|')})\\b|${bannedPaths.join('|')}`, 'i');
  const repoName = 'job-queue-' + 'hen' + 'icos';
  const legacyFile = 'project-' + 'hen' + 'icos';
  const dialectFile = 'target-' + 'dialect';
  const forbiddenDep = new RegExp(
    `[\\\\/](plugins|${repoName})[\\\\/]|${legacyFile}\\.js$|${dialectFile}\\.js$|\\.ts$`,
  );

  for (const rel of engineFiles) {
    const filePath = path.join(__dirname, rel);
    const content = fs.readFileSync(filePath, 'utf8');
    assert.ok(!forbiddenEngineRef.test(content), `engine module must not reference project/plugin data: ${rel}`);
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
      assert.ok(!forbiddenDep.test(mod.filename), `${rel} loaded forbidden dependency: ${mod.filename}`);
      for (const child of mod.children || []) {
        stack.push(child);
      }
    }
  }

  // Clean 0-plugin createEngine dependency check
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-dep-'));
  try {
    const configPath = path.join(tmpDir, 'job-queue.config.json');
    fs.writeFileSync(configPath, JSON.stringify({ version: 1, entry: 'q.js', plugins: [] }));
    createEngine({ configPath: fs.realpathSync.native(configPath), tolerant: false }).check('echo ok');
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

/** Built-in plugins never import engine internals, repository plugins, `.ts` files, or sibling built-in plugins. */
function checkBuiltInPluginIsolation(assert) {
  const entries = fs.readdirSync(PluginsRoot, { withFileTypes: true }).filter((e) => e.isDirectory());
  const pluginNames = entries.map((e) => e.name);
  const repoPluginRef = 'job-queue-' + 'hen' + 'icos';
  for (const dirName of pluginNames) {
    const dirPath = path.join(PluginsRoot, dirName);
    const jsFiles = fs.readdirSync(dirPath).filter((f) => f.endsWith('.js'));
    for (const file of jsFiles) {
      const text = fs.readFileSync(path.join(dirPath, file), 'utf8');
      assert.ok(
        !/require\([^)]*check-queued-command|check-queued-command[\\/]/.test(text),
        `${dirName}/${file} must not import engine internals`,
      );
      assert.ok(!text.includes(repoPluginRef), `${dirName}/${file} must not reference repo plugin`);
      assert.ok(!/\.ts['"`]/.test(text), `${dirName}/${file} must not import .ts`);
      for (const other of pluginNames) {
        if (other !== dirName) {
          assert.ok(
            !new RegExp(`plugins[\\\\/]${other}|\\.\\.[\\\\/]${other}`).test(text),
            `${dirName}/${file} must not import sibling plugin ${other}`,
          );
        }
      }
    }
  }
}

function copyJsTree(srcDir, dstDir) {
  fs.mkdirSync(dstDir, { recursive: true });
  for (const entry of fs.readdirSync(srcDir, { withFileTypes: true })) {
    const srcPath = path.join(srcDir, entry.name);
    const dstPath = path.join(dstDir, entry.name);
    if (entry.isDirectory()) {
      copyJsTree(srcPath, dstPath);
    } else if (entry.name.endsWith('.js')) {
      fs.copyFileSync(srcPath, dstPath);
    }
  }
}

/**
 * An agent that treats a non-zero exit code as a denial stops the command when a hook fails, so --hook must allow
 * (no output, exit code 0) and log when a module fails to load, the judgement throws or the input is not JSON; --text
 * must still fail. Runs copies of the entry and active plugins in a temporary checkout whose modules are broken on
 * purpose (the log lands in that checkout).
 */
function checkHookFailsOpen(assert) {
  const real = (name) => JSON.stringify(path.join(__dirname, name));
  const loaded = loadConfigFile(path.resolve(__dirname, '..'));
  assert.strictEqual(loaded.status, 'ok', 'active job-queue.config.json must load cleanly');
  const configRoot = path.dirname(loaded.path);
  const deniedCmd = ['dot' + 'net', 'test', 'X'].join(' ');
  const payload = JSON.stringify({ toolCall: { name: 'run_command', args: { CommandLine: deniedCmd } } });
  const cases = [
    {
      name: 'a module that fails to load',
      modules: { 'plugin-loader.js': "throw new Error('broken loader');" },
      logged: 'broken loader',
      textFails: true,
    },
    {
      name: 'a judgement that throws',
      modules: {
        'command-rules.js':
          `const real = require(${real('command-rules.js')});\n` +
          'module.exports = { createRules: (cfg, opts) => ({ ...real.createRules(cfg, opts), check: () => {\n' +
          "  throw new Error('broken rule');\n} }) };",
      },
      logged: 'broken rule',
      textFails: true,
    },
    { name: 'input that is not JSON', modules: {}, input: deniedCmd, logged: 'invalid hook input' },
    { name: 'an empty input', modules: {}, input: '', logged: 'invalid hook input' },
    // The same copies answer a valid input as the hook does: the denial is printed in full and nothing is logged
    { name: 'a valid input', modules: {}, logged: null, printed: '{"decision":"deny"' },
  ];
  for (const testCase of cases) {
    const root = fs.realpathSync.native(fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-fail-open-')));
    try {
      const entry = path.join(root, 'scripts', 'agent', 'check-queued-command.js');
      const engineDir = path.join(root, 'scripts', 'agent', 'check-queued-command');
      const libDir = path.join(root, 'scripts', 'human', 'job-queue', 'lib');
      fs.mkdirSync(path.join(engineDir, 'rules'), { recursive: true });
      fs.mkdirSync(libDir, { recursive: true });
      fs.copyFileSync(path.join(__dirname, '..', 'check-queued-command.js'), entry);
      for (const f of ['plugin-loader.js', 'command-rules.js', 'queue-rewrite.js', 'hook.js', 'shell-reader.js']) {
        fs.copyFileSync(path.join(__dirname, f), path.join(engineDir, f));
      }
      for (const f of ['wrappers.js', 'node-scripts.js']) {
        fs.copyFileSync(path.join(__dirname, 'rules', f), path.join(engineDir, 'rules', f));
      }
      for (const f of fs.readdirSync(path.join(__dirname, '..', '..', 'human', 'job-queue', 'lib'))) {
        if (f.endsWith('.js')) {
          fs.copyFileSync(path.join(__dirname, '..', '..', 'human', 'job-queue', 'lib', f), path.join(libDir, f));
        }
      }
      fs.copyFileSync(loaded.path, path.join(root, 'job-queue.config.json'));
      for (const plugin of loaded.raw.plugins) {
        const srcFolder = resolvePluginFolder(plugin.name, configRoot);
        if (srcFolder && fs.existsSync(srcFolder)) {
          copyJsTree(srcFolder, path.join(root, path.relative(configRoot, srcFolder)));
        }
      }
      for (const [file, text] of Object.entries(testCase.modules)) {
        fs.writeFileSync(path.join(engineDir, file), `'use strict';\n${text}\n`);
      }
      const input = testCase.input === undefined ? payload : testCase.input;
      const hook = spawnSync(process.execPath, [entry, '--hook'], { input, encoding: 'utf8' });
      assert.strictEqual(hook.status, 0, `--hook with ${testCase.name} exits 0: ${hook.stderr}`);
      const logFile = path.join(root, 'tmp', 'hook-errors.log');
      if (testCase.logged === null) {
        const okOut = hook.stdout.startsWith(testCase.printed) && hook.stdout.endsWith('}');
        assert.ok(okOut, `--hook prints ${hook.stdout}`);
        assert.ok(!fs.existsSync(logFile), `--hook with ${testCase.name} logs nothing`);
        continue;
      }
      assert.strictEqual(hook.stdout, '', `--hook with ${testCase.name} prints nothing`);
      const log = fs.readFileSync(logFile, 'utf8');
      assert.ok(log.includes(testCase.logged), `--hook with ${testCase.name} logs it: ${log}`);
      if (testCase.textFails) {
        const text = spawnSync(process.execPath, [entry, '--text', deniedCmd], { encoding: 'utf8' });
        assert.ok(text.status !== 0 && text.stderr.includes(testCase.logged), `--text with ${testCase.name} fails`);
      }
    } finally {
      fs.rmSync(root, { recursive: true, force: true });
    }
  }
}

/** Loads the active repository job-queue.config.json in strict mode and runs every plugin's hook-checks.js. */
function runActiveConfigChecks(assert, createEngine) {
  createEngine({ tolerant: false });
  const loaded = loadConfigFile(path.resolve(__dirname, '..'));
  assert.strictEqual(loaded.status, 'ok', 'active job-queue.config.json must load cleanly');
  const configRoot = path.dirname(loaded.path);
  for (const entry of loaded.raw.plugins) {
    const folder = resolvePluginFolder(entry.name, configRoot);
    assert.ok(folder && fs.existsSync(folder), `configured plugin folder exists: ${entry.name}`);
    const checksFile = path.join(folder, 'hook-checks.js');
    if (fs.existsSync(checksFile)) {
      const mod = require(fs.realpathSync.native(checksFile));
      assert.strictEqual(typeof mod.checks, 'function', `${entry.name}/hook-checks.js must export checks()`);
      mod.checks(assert, { createEngine });
    }
  }
}

module.exports = { selfTest };
