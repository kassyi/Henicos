'use strict';

// Failure-handling checks of the check-queued-command engine, run by self-test.js: tolerant (--hook) and strict
// (--text, exports) handling of broken configs, plugins, wording and agent plugins, in a copied checkout and in
// temporary configs.
//
// Usage: require('./self-test-failures.js') returns { checkTolerantAndStrictModes, checkPluginFailuresKeepDenials },
// each called as (assert, createEngine[, createNeutralWorkspace]).

const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { loadHookConfig } = require('./plugin-loader.js');
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
function checkPluginFailuresKeepDenials(assert, createEngine, createNeutralWorkspace) {
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

module.exports = { checkTolerantAndStrictModes, checkPluginFailuresKeepDenials };
