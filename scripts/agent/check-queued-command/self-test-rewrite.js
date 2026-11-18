'use strict';

// Neutral self-test of the generic rewrite engine (queue-rewrite.js and hookAnswer of hook.js): verifies queuing,
// quoting round-trips, tool_input field preservation, trailing # intent comments, build/test split delegation,
// plain denials, undecided notes, and malformed plugin rewrite validation without any repo or plugin identifiers.

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { queuedText } = require('./queue-rewrite.js');

const Queue = 'node tools/custom-queue.js run ';

function queueOptions(rewritten) {
  return rewritten.slice(Queue.length, rewritten.indexOf(' -- ')).split(' ');
}

function createNeutralFixtures(tmpDir) {
  const root = fs.realpathSync.native(tmpDir);
  fs.mkdirSync(path.join(root, 'tools'), { recursive: true });
  fs.writeFileSync(path.join(root, 'tools', 'custom-queue.js'), '// stub\n');

  const pluginDir = path.join(root, 'neutral-plugin');
  fs.mkdirSync(pluginDir, { recursive: true });
  fs.writeFileSync(
    path.join(pluginDir, 'hook-rules.js'),
    [
      "'use strict';",
      'module.exports = {',
      "  name: './neutral-plugin',",
      '  rules() {',
      '    return [{',
      "      matches: ({ name }) => name === 'buildtool',",
      '      reason: (_r, { args }) => {',
      "        const verb = args[0] || '';",
      "        const hit = ['build', 'test', 'watch'].includes(verb);",
      "        return hit ? { tool: 'buildtool', verb, text: 'buildtool ' + verb } : null;",
      '      },',
      '    }];',
      '  },',
      '  splitForRewrite(segments, reader) {',
      '    const builds = [];',
      '    const tests = [];',
      '    for (const raw of segments) {',
      '      const seg = raw.trim();',
      '      const list = reader.words(seg).map(reader.unquote);',
      "      if (list[0] !== 'buildtool') return null;",
      "      if (list[1] === 'build' && tests.length === 0) builds.push(seg);",
      "      else if (list[1] === 'test' && list.includes('--no-build')) tests.push(seg);",
      '      else return null;',
      '    }',
      "    const ok = builds.length > 0 && tests.length > 0;",
      "    return ok ? { builds: builds.join(' && '), tests: tests.join(' && ') } : null;",
      '  },',
      '  rewrite(segment, list, reason, context) {',
      "    if (!/^buildtool(\\.exe)?$/i.test(list[0] || '')) return null;",
      "    if (reason.verb === 'build') return { gpu: false, options: [] };",
      "    if (reason.verb === 'test') {",
      "      if (segment.includes('GpuSuite')) return { gpu: true, options: [] };",
      "      if (segment.includes('CpuSuite')) return { gpu: false, options: [] };",
      "      return { undecided: 'unknown suite' };",
      '    }',
      '    return null;',
      '  },',
      '};',
    ].join('\n'),
  );

  const ioDir = path.join(root, 'neutral-io');
  fs.mkdirSync(ioDir, { recursive: true });
  fs.writeFileSync(
    path.join(ioDir, 'hook-io.js'),
    [
      "'use strict';",
      'module.exports = {',
      "  name: './neutral-io',",
      '  read(payload) {',
      "    if (!payload || typeof payload !== 'object') return null;",
      '    if (payload.noRewrite !== undefined) {',
      "      return { command: String(payload.noRewrite || ''), kind: 'pwsh', canRewrite: false };",
      '    }',
      '    if (payload.tool_input !== undefined || payload.tool_name !== undefined) {',
      '      const ti = payload.tool_input || {};',
      "      const kind = payload.tool_name === 'PowerShell' ? 'pwsh' : 'bash';",
      "      return { command: String(ti.command || ''), kind, cwd: payload.cwd, canRewrite: true, toolInput: ti };",
      '    }',
      '    return null;',
      '  },',
      '  deny(_read, reasonText) {',
      "    return { decision: 'deny', reason: reasonText };",
      '  },',
      '  rewrite(read, command, additionalContext) {',
      "    if (!read.canRewrite) return null;",
      '    return { updatedInput: { ...read.toolInput, command }, additionalContext };',
      '  },',
      '};',
    ].join('\n'),
  );

  const configPath = path.join(root, 'job-queue.config.json');
  fs.writeFileSync(
    configPath,
    JSON.stringify({
      version: 1,
      entry: 'tools/custom-queue.js',
      plugins: [{ name: './neutral-plugin' }, { name: './neutral-io' }],
    }),
  );
  return { root, configPath: fs.realpathSync.native(configPath) };
}

function checkRewrites(assert, createEngine) {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-rewrite-neutral-'));
  try {
    const { root, configPath } = createNeutralFixtures(tmpDir);
    const engine = createEngine({ configPath, tolerant: false });
    const { check, hookAnswer } = engine;
    const answer = (tool, command, extra = {}) =>
      hookAnswer({ tool_name: tool, tool_input: { command, ...extra }, cwd: root });

    const rewritten = [
      ['Bash', 'buildtool test CpuSuite --no-build', false, []],
      ['Bash', 'buildtool test GpuSuite', true, []],
      ['Bash', 'buildtool build Solution.slnx', false, []],
      ['Bash', 'buildtool build A && buildtool test GpuSuite', true, []],
      ['Bash', "buildtool test CpuSuite --filter \"A|B\" -p:X='it'\"'\"'s' > tmp/log.txt 2>&1", false, []],
      ['PowerShell', "buildtool test CpuSuite --filter \"Name~it's\" -p:Y='a b'", false, []],
      ['PowerShell', 'buildtool build X && buildtool test GpuSuite', true, []],
    ];
    for (const [tool, command, gpu, options] of rewritten) {
      const kind = tool === 'PowerShell' ? 'pwsh' : 'bash';
      const output = answer(tool, command);
      assert.ok(output.updatedInput !== undefined, `neutral rewrite in ${kind}: ${command}`);
      const queued = output.updatedInput.command;
      assert.ok(queued.startsWith(Queue), `starts with queue: ${queued}`);
      assert.deepStrictEqual(check(queued, kind), [], `queued form passes hook: ${queued}`);
      assert.strictEqual(queuedText(queued, kind), command, `quoting round-trips in ${kind}: ${queued}`);
      assert.deepStrictEqual(queueOptions(queued), [...(gpu ? ['--gpu'] : []), ...options, '--shell', kind]);
    }

    // Extra tool_input fields preserved
    const fields = { timeout: 600000, run_in_background: true, description: 'Run' };
    const fieldOut = answer('Bash', 'buildtool test CpuSuite', fields);
    assert.deepStrictEqual(fieldOut.updatedInput, { command: fieldOut.updatedInput.command, ...fields });
    assert.ok(fieldOut.additionalContext.includes(fieldOut.updatedInput.command));

    // Trailing # intent comment preserved outside quotes
    const intent = answer('Bash', 'buildtool test CpuSuite # intent: run CPU suite');
    assert.ok(intent.updatedInput.command.endsWith("CpuSuite' # intent: run CPU suite"));
    assert.strictEqual(queuedText(intent.updatedInput.command, 'bash'), 'buildtool test CpuSuite');

    // Split build and test
    const splitOut = answer('Bash', 'buildtool build A && buildtool test GpuSuite --no-build # intent: gpu');
    const at = splitOut.updatedInput.command.indexOf(` && ${Queue}`);
    assert.ok(at > 0, 'split builds into two queued runs');
    const first = splitOut.updatedInput.command.slice(0, at);
    const second = splitOut.updatedInput.command.slice(at + ' && '.length);
    assert.deepStrictEqual(queueOptions(first), ['--shell', 'bash']);
    assert.deepStrictEqual(queueOptions(second), ['--gpu', '--shell', 'bash']);
    assert.strictEqual(queuedText(first, 'bash'), 'buildtool build A');
    assert.ok(second.endsWith(' # intent: gpu'));
    assert.strictEqual(queuedText(second, 'bash'), 'buildtool test GpuSuite --no-build');
    assert.ok(/held only for the tests/.test(splitOut.additionalContext));

    // Plain denials
    const plain = [
      ['Bash', 'buildtool test CpuSuite | tail -5'],
      ['Bash', 'buildtool test CpuSuite; echo done'],
      ['Bash', 'buildtool test CpuSuite || echo failed'],
      ['Bash', 'buildtool build X &'],
      ['Bash', 'buildtool build X\nbuildtool test CpuSuite'],
      ['Bash', 'buildtool test $(echo CpuSuite)'],
      ['Bash', 'buildtool test `echo CpuSuite`'],
      ['Bash', 'buildtool test CpuSuite < input.txt'],
      ['Bash', 'cmd /c "buildtool test CpuSuite"'],
      ['Bash', '(buildtool build X)'],
      ['Bash', 'timeout 600 buildtool build X'],
      ['Bash', '"C:\\Program Files\\buildtool\\buildtool.exe" build X'],
      ['Bash', 'buildtool watch test'],
      ['Bash', 'X=/foo buildtool build X'],
      ['Bash', 'node tools/custom-queue.js run -- "buildtool build X" && buildtool test CpuSuite'],
      ['PowerShell', '$o = buildtool test CpuSuite'],
      ['PowerShell', 'buildtool test CpuSuite --% -p:X=1'],
    ];
    for (const [tool, command] of plain) {
      const denial = answer(tool, command);
      assert.strictEqual(denial.updatedInput, undefined, `plain denial: ${command}`);
      assert.strictEqual(denial.decision, 'deny');
      assert.ok(!/--gpu when it does/.test(denial.reason));
    }

    // Missing queue entry in cwd stays denied
    const elsewhere = hookAnswer({
      tool_name: 'Bash',
      tool_input: { command: 'buildtool build X' },
      cwd: os.tmpdir(),
    });
    assert.strictEqual(elsewhere.decision, 'deny');

    // Non-rewriting agent stays denied
    const noRewrite = hookAnswer({ noRewrite: 'buildtool build X' });
    assert.strictEqual(noRewrite.decision, 'deny');
    assert.strictEqual(noRewrite.updatedInput, undefined);

    // Undecided note
    const undecided = answer('Bash', 'buildtool test UnknownSuite');
    assert.strictEqual(undecided.decision, 'deny');
    assert.ok(/--gpu when it does/.test(undecided.reason));
    assert.ok(undecided.reason.includes('unknown suite'));

    // Malformed plugin rewrite return throws in strict mode
    const badDir = path.join(root, 'bad-rewrite');
    fs.mkdirSync(badDir, { recursive: true });
    fs.writeFileSync(
      path.join(badDir, 'hook-rules.js'),
      [
        "'use strict';",
        'module.exports = {',
        "  name: './bad-rewrite',",
        "  rules: () => [{ matches: ({ name }) => name === 'buildtool',",
        "    reason: () => ({ tool: 'b', verb: 't', text: 'buildtool test' }) }],",
        "  rewrite: () => ({ bad: true }),",
        '};',
      ].join('\n'),
    );
    const badConfig = path.join(root, 'bad-rewrite.json');
    fs.writeFileSync(
      badConfig,
      JSON.stringify({
        version: 1,
        entry: 'tools/custom-queue.js',
        plugins: [{ name: './bad-rewrite' }, { name: './neutral-io' }],
      }),
    );
    const badEngine = createEngine({ configPath: fs.realpathSync.native(badConfig), tolerant: false });
    assert.throws(
      () => badEngine.hookAnswer({ tool_name: 'Bash', tool_input: { command: 'buildtool test' }, cwd: root }),
      /rewrite\(\) must return/,
      'malformed plugin rewrite return throws in strict mode',
    );
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

module.exports = { checkRewrites };
