'use strict';

// The self-test of check-queued-command.js (--self-test): commands that must be denied and commands that must pass,
// per dialect, written out and generated from templates.

const { check } = require('./command-rules.js');

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
  assert.notDeepStrictEqual(check('just profile=cpu fuzz-diff'), [], 'just variable before the recipe');
  checkGeneratedForms(assert);
  process.stdout.write('check-queued-command self-test passed\n');
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

module.exports = { selfTest };
