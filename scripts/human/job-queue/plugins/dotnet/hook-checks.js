'use strict';

// Hook rule and rewrite checks for the built-in dotnet plugin.
//
// Usage: invoked by `node scripts/agent/check-queued-command.js --self-test`.

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const dotnetPlugin = require('./hook-rules.js');

const Templates = {
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

function fillTemplate(template, build) {
  const before = template.slice(0, template.indexOf('{B}'));
  const single = (before.match(/'/g) || []).length % 2 === 1;
  const double = !single && (before.match(/"/g) || []).length % 2 === 1;
  const replaced = single ? build.replace(/'/g, '') : double ? build.replace(/"/g, "'") : build;
  return template.split('{B}').join(replaced);
}

function withDotnetConfig(fn) {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-dotnet-'));
  try {
    const configPath = path.join(tmpDir, 'job-queue.config.json');
    fs.writeFileSync(
      configPath,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [{ name: 'dotnet' }],
      }),
    );
    return fn(fs.realpathSync.native(configPath), fs.realpathSync.native(tmpDir));
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

function checks(assert, { createEngine }) {
  assert.deepStrictEqual(dotnetPlugin.runnerTargets, ['dotnet', 'msbuild'], 'dotnet runnerTargets');
  assert.throws(() => dotnetPlugin.rules(123), /dotnet plugin options/, 'dotnet invalid options throws');

  withDotnetConfig((configPath, tmpDir) => {
    const engine = createEngine({ configPath, tolerant: false });
    const { check, checkDetailed } = engine;

    const denied = [
      'dotnet test tests/X.csproj',
      'cd tmp/w && dotnet build Henicos.slnx -nodeReuse:false',
      'DiffEngine_Disabled=true dotnet test',
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
      'Start-Process dotnet -ArgumentList "test x" -Wait',
      'dotnet watch test',
      'dotnet -p:Foo="a b" test',
      'FOO="a b" dotnet test',
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
      'Start-Process dotnet "test x"',
      'dotnet -d build',
      'sudo dotnet test',
      'ls | xargs -n1 dotnet build',
      'Invoke-Expression "dotnet test"',
      '! dotnet build',
      'valgrind dotnet test',
      'strace dotnet build',
      'ltrace dotnet build',
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
      'dotnet pack',
      '"$X/msbuild.exe" foo.sln',
      '$MSB/msbuild x',
      '"$X/dotnet" test',
    ];
    for (const command of denied) {
      assert.notDeepStrictEqual(check(command), [], `dotnet should deny: ${command}`);
    }

    const allowed = [
      'git commit -m "run dotnet test before merging"',
      'dotnet build-server shutdown',
      'dotnet --info',
      'msg="fix: dotnet build failure"; git commit -m "$msg"',
      'dotnet run --project src/Compiler/Kassyi.GSharp.Compiler.Generators -- demangle log.txt',
      'dotnet test --help',
      'dotnet tool list',
      'dotnet tool restore',
      'dotnet tool install -g dotnet-counters',
      'dnx dotnet-counters',
      'grep -n "dotnet test" justfile',
      'grep -E "foo|dotnet test" justfile',
      'rg "x; dotnet build" docs',
      'echo "a && dotnet test"',
      "cat > tmp/notes.md <<'EOF'\ndotnet build Henicos.slnx\njust test\nEOF",
      'cat > tmp/notes.md <<-EOF\n\tdotnet build x\n\tEOF',
      "$body = @'\ndotnet test\n'@\ngh issue comment 1 --body $body",
      '$msg = "fix; dotnet test now"; git commit -m $msg',
      'rm -rf obj # intent: clean first; dotnet build checks it',
      'ls # && dotnet test',
      'dotnet-trace ps',
      'dotnet-counters ps',
      'dotnet-coverage --help',
      'command -v dotnet',
      'dotnet pack --help',
      '"$DOTNET" test',
    ];
    for (const command of allowed) {
      assert.deepStrictEqual(check(command), [], `dotnet should allow: ${command}`);
    }

    const deniedPowerShell = [
      'Set-Location "tmp\\"; dotnet test',
      'Remove-Item -Recurse -Force "obj\\"; dotnet build',
      'Write-Output "it\'s starting"; Write-Output "$(dotnet test)"',
      '$out = dotnet test',
      '$x=dotnet build',
      '@(dotnet test)',
      '$r = @(dotnet test)',
      '$o = (dotnet test)',
      '$env:CI = "true"; dotnet test',
      '& "C:\\Program Files\\dotnet\\dotnet.exe" test',
      '$o = & "C:\\Program Files\\dotnet\\dotnet.exe" test',
      'if ($x) { & "C:\\x\\dotnet.exe" test }',
      '(& "C:\\x\\dotnet.exe" test)',
      '$o = (& "C:\\x\\dotnet.exe" build)',
      'Get-ChildItem | ForEach-Object { & "C:\\x\\dotnet.exe" test $_ }',
      '. "C:\\x\\dotnet.exe" test',
      'git commit -m "$(dotnet test)"',
    ];
    for (const command of deniedPowerShell) {
      assert.notDeepStrictEqual(check(command, 'pwsh'), [], `dotnet pwsh should deny: ${command}`);
    }

    const allowedPowerShell = [
      'Test-Path "src\\Compiler\\"; Write-Output "fix; dotnet test"',
      "gh pr comment 1 --body @'\nIt's fine. Run $(dotnet test) later.\n'@",
      'git status # see $(dotnet test)',
      'if ($true) { Write-Output "a `" { dotnet test `"" }',
      "Write-Output 'it''s $(dotnet test)'",
      '$msg="dotnet test"',
      '$tool = "msbuild"',
    ];
    for (const command of allowedPowerShell) {
      assert.deepStrictEqual(check(command, 'pwsh'), [], `dotnet pwsh should allow: ${command}`);
    }

    assert.notDeepStrictEqual(check('echo "x\\"; dotnet test', 'cmd'), [], 'dotnet cmd: "x\\" ends quote');
    assert.notDeepStrictEqual(check('call dotnet build', 'cmd'), [], 'dotnet cmd: call prefix');

    const builds = [
      'dotnet build',
      'dotnet test tests/X.csproj --filter "A|B"',
      'dotnet -d build -p:Foo="a b"',
      'msbuild Henicos.slnx',
    ];
    for (const build of builds) {
      for (const t of Templates.runsBash) {
        const command = fillTemplate(t, build);
        assert.notDeepStrictEqual(check(command, 'bash'), [], `dotnet form deny bash: ${command}`);
      }
      for (const t of Templates.runsPwsh) {
        const command = fillTemplate(t, build);
        assert.notDeepStrictEqual(check(command, 'pwsh'), [], `dotnet form deny pwsh: ${command}`);
      }
      for (const t of Templates.namesBash) {
        const command = fillTemplate(t, build);
        assert.deepStrictEqual(check(command, 'bash'), [], `dotnet form allow bash: ${command}`);
      }
      for (const t of Templates.namesPwsh) {
        const command = fillTemplate(t, build);
        assert.deepStrictEqual(check(command, 'pwsh'), [], `dotnet form allow pwsh: ${command}`);
      }
    }

    const detailed = [
      ['dotnet test tests/X.csproj', 'bash', [{ tool: 'dotnet', verb: 'test', text: 'dotnet test' }]],
      ['dotnet stryker', 'bash', [{ tool: 'dotnet', verb: 'stryker', text: 'dotnet stryker' }]],
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
    ];
    for (const [command, kind, expected] of detailed) {
      assert.deepStrictEqual(checkDetailed(command, kind), expected, `dotnet checkDetailed: ${command}`);
    }

    // Rewrite & splitForRewrite checks without repository plugin
    assert.deepStrictEqual(
      dotnetPlugin.rewrite(
        'dotnet build X',
        ['dotnet', 'build', 'X'],
        { tool: 'dotnet', verb: 'build' },
        { root: tmpDir },
      ),
      { gpu: false, options: [] },
      'dotnet build rewrite',
    );
    assert.deepStrictEqual(
      dotnetPlugin.rewrite(
        'dotnet msbuild -t:VSTest',
        ['dotnet', 'msbuild', '-t:VSTest'],
        { tool: 'dotnet', verb: 'msbuild' },
        { root: tmpDir },
      ),
      { undecided: 'it runs an MSBuild target named like a test target' },
      'dotnet msbuild -t:VSTest undecided',
    );
    assert.deepStrictEqual(
      dotnetPlugin.rewrite(
        'dotnet test X',
        ['dotnet', 'test', 'X'],
        { tool: 'dotnet', verb: 'test' },
        { root: tmpDir },
      ),
      { undecided: 'no repository plugin determines whether this test project uses the GPU' },
      'dotnet test without repository plugin is undecided',
    );
    assert.strictEqual(
      dotnetPlugin.rewrite(
        '"C:\\Program Files\\dotnet\\dotnet.exe" build',
        ['"C:\\Program Files\\dotnet\\dotnet.exe"', 'build'],
        { tool: 'dotnet', verb: 'build' },
        { root: tmpDir },
      ),
      null,
      'quoted dotnet path is not rewritten',
    );
    assert.strictEqual(
      dotnetPlugin.rewrite('dotnet watch test', ['dotnet', 'watch', 'test'], { tool: 'dotnet', verb: 'watch' }, {}),
      null,
      'dotnet watch is not rewritten',
    );
    assert.ok(
      dotnetPlugin.api.isTestInvocation(['dotnet', 'test', 'X'], { tool: 'dotnet', verb: 'test' }),
      'api.isTestInvocation true for dotnet test',
    );
    assert.ok(
      !dotnetPlugin.api.isTestInvocation(['"C:\\x\\dotnet.exe"', 'test'], { tool: 'dotnet', verb: 'test' }),
      'api.isTestInvocation false for quoted dotnet path',
    );
  });
}

module.exports = { checks };
