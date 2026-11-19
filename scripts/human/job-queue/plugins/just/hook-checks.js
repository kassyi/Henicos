'use strict';

// Hook rule, options validation, and justfile rewrite checks for the built-in just plugin.
//
// Usage: invoked by `node scripts/agent/check-queued-command.js --self-test`.

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const justPlugin = require('./hook-rules.js');

const DefaultOptions = {
  hookRecipes: '^(test\\S*|coverage-diff|fuzz-\\S+|mutate|long-checks|bench\\S*|doctest)$',
  hookDryRunRecipes: '^(mutate|long-checks)$',
};

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

function withJustConfig(fn, options = DefaultOptions) {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-just-'));
  try {
    const configPath = path.join(tmpDir, 'job-queue.config.json');
    fs.writeFileSync(
      configPath,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [{ name: 'just', options }],
      }),
    );
    fs.writeFileSync(
      path.join(tmpDir, 'justfile'),
      [
        'queue := "node scripts/human/job-queue.js run"',
        '',
        'test *args:',
        '  {{queue}} --gpu -- dotnet test {{args}}',
        '',
        'test-differential-cpu *args:',
        '  {{queue}} -- dotnet test Cpu {{args}}',
        '',
        'fuzz-diff *args:',
        '  {{queue}} --weight 2 -- node scripts/human/fuzz-differential.js {{args}}',
        '',
        'test-category category:',
        '  {{queue}} --gpu -- dotnet test {{category}}',
        '',
        'test-memory project:',
        '  node scripts/human/test-memory.js {{project}}',
        '',
        'mutate phase="compiler":',
        '  {{queue}} {{if phase == "compiler-cuda" { "--gpu" } else { "" }}} -- node scripts/human/mutate.js',
        '',
      ].join('\n'),
    );
    return fn(fs.realpathSync.native(configPath), fs.realpathSync.native(tmpDir));
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

function checks(assert, { createEngine }) {
  assert.deepStrictEqual(justPlugin.runnerTargets, ['just'], 'just runnerTargets');

  // Strict options validation (direct and through createEngine)
  assert.throws(() => justPlugin.validateOptions(undefined), /just plugin options must be an object/);
  assert.throws(() => justPlugin.validateOptions({ hookDryRunRecipes: '^a$' }), /hookRecipes/);
  assert.throws(() => justPlugin.validateOptions({ hookRecipes: 123, hookDryRunRecipes: '^a$' }), /hookRecipes/);
  assert.throws(() => justPlugin.validateOptions({ hookRecipes: '[', hookDryRunRecipes: '^a$' }), /hookRecipes/);
  assert.throws(() => justPlugin.validateOptions({ hookRecipes: '^a$' }), /hookDryRunRecipes/);
  assert.throws(() => justPlugin.validateOptions({ hookRecipes: '^a$', hookDryRunRecipes: '' }), /hookDryRunRecipes/);
  assert.throws(() => justPlugin.validateOptions({ hookRecipes: '^a$', hookDryRunRecipes: '(' }), /hookDryRunRecipes/);

  withJustConfig((configPath) => {
    assert.throws(
      () => createEngine({ configPath, tolerant: false }),
      /hookRecipes/,
      'createEngine strict rejects numeric hookRecipes',
    );
  }, { hookRecipes: 123, hookDryRunRecipes: '^mutate$' });

  withJustConfig((configPath, tmpDir) => {
    const engine = createEngine({ configPath, tolerant: false });
    const { check, checkDetailed } = engine;

    const denied = [
      'just test --filter Foo > tmp/log.txt 2>&1',
      'just test-category Runtime',
      'just -f justfile test',
      'bash -c "cd x && just test"',
      'just args="a b" test',
      '(just test)',
      'just coverage-diff',
      'just long-checks',
      'just bench',
      'just bench-x',
      'just doctest',
      '"$X/just" test',
    ];
    for (const command of denied) {
      assert.notDeepStrictEqual(check(command), [], `just should deny: ${command}`);
    }

    const allowed = [
      'gh issue create --body "just test fails"',
      'just env-info',
      'just --list',
      'just --dry-run coverage-diff',
      'just long-checks --dry-run',
      'just --show bench',
      'just --dry-run bench-x',
      'just --dry-run doctest',
    ];
    for (const command of allowed) {
      assert.deepStrictEqual(check(command), [], `just should allow: ${command}`);
    }

    assert.notDeepStrictEqual(check('$res = just test', 'pwsh'), [], 'just pwsh: $res = just test');
    assert.notDeepStrictEqual(check('$o = (just test)', 'pwsh'), [], 'just pwsh: $o = (just test)');
    assert.notDeepStrictEqual(check('just profile=cpu fuzz-diff'), [], 'just variable before the recipe');

    const builds = ['just test', 'just -f justfile test-category Runtime'];
    for (const build of builds) {
      for (const t of Templates.runsBash) {
        const command = fillTemplate(t, build);
        assert.notDeepStrictEqual(check(command, 'bash'), [], `just form deny bash: ${command}`);
      }
      for (const t of Templates.runsPwsh) {
        const command = fillTemplate(t, build);
        assert.notDeepStrictEqual(check(command, 'pwsh'), [], `just form deny pwsh: ${command}`);
      }
      for (const t of Templates.namesBash) {
        const command = fillTemplate(t, build);
        assert.deepStrictEqual(check(command, 'bash'), [], `just form allow bash: ${command}`);
      }
      for (const t of Templates.namesPwsh) {
        const command = fillTemplate(t, build);
        assert.deepStrictEqual(check(command, 'pwsh'), [], `just form allow pwsh: ${command}`);
      }
    }

    assert.deepStrictEqual(
      checkDetailed('just test-category Runtime', 'bash'),
      [{ tool: 'just', verb: 'test-category', text: 'just test-category' }],
      'just checkDetailed test-category',
    );
    assert.deepStrictEqual(
      checkDetailed('valgrind just test', 'bash'),
      [{ tool: 'just', verb: 'test', text: 'just test' }],
      'just checkDetailed valgrind',
    );

    // just rewrite behavior
    const ctx = { root: tmpDir, changedDirectory: false, entry: 'scripts/human/job-queue.js' };
    assert.deepStrictEqual(
      justPlugin.rewrite('just test', ['just', 'test'], { tool: 'just', verb: 'test' }, ctx),
      { gpu: true, options: [] },
      'just test rewrite --gpu',
    );
    assert.deepStrictEqual(
      justPlugin.rewrite(
        'just test-differential-cpu',
        ['just', 'test-differential-cpu'],
        { tool: 'just', verb: 'test-differential-cpu' },
        ctx,
      ),
      { gpu: false, options: [] },
      'just test-differential-cpu rewrite no --gpu',
    );
    assert.deepStrictEqual(
      justPlugin.rewrite(
        'just fuzz-diff cpu 5',
        ['just', 'fuzz-diff', 'cpu', '5'],
        { tool: 'just', verb: 'fuzz-diff' },
        ctx,
      ),
      { gpu: false, options: ['--weight', '2'] },
      'just fuzz-diff --weight 2',
    );
    assert.deepStrictEqual(
      justPlugin.rewrite(
        'just test-memory X',
        ['just', 'test-memory', 'X'],
        { tool: 'just', verb: 'test-memory' },
        ctx,
      ),
      { undecided: 'just test-memory does not queue itself' },
      'just test-memory undecided',
    );
    assert.deepStrictEqual(
      justPlugin.rewrite('just mutate compiler', ['just', 'mutate', 'compiler'], { tool: 'just', verb: 'mutate' }, ctx),
      { undecided: 'just mutate chooses its queue options from its arguments' },
      'just mutate undecided',
    );
    assert.strictEqual(
      justPlugin.rewrite(
        'just test',
        ['just', 'test'],
        { tool: 'just', verb: 'test' },
        { ...ctx, changedDirectory: true },
      ),
      null,
      'just after cd returns null',
    );
    assert.strictEqual(
      justPlugin.rewrite('just.exe test', ['"C:\\just.exe"', 'test'], { tool: 'just', verb: 'test' }, ctx),
      null,
      'quoted just path returns null',
    );
  });
}

module.exports = { checks };
