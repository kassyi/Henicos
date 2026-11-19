'use strict';

// Hook rule checks for the built-in nvidia-gpu plugin using a neutral fake target plugin (zero cross-plugin deps).
//
// Usage: invoked by `node scripts/agent/check-queued-command.js --self-test`.

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const nvidiaPlugin = require('./hook-rules.js');

function withNeutralTargetConfig(fn) {
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'cqc-nvidia-'));
  try {
    const fakeDir = path.join(tmpDir, 'fake-target');
    fs.mkdirSync(fakeDir, { recursive: true });
    fs.writeFileSync(
      path.join(fakeDir, 'hook-rules.js'),
      [
        "'use strict';",
        'module.exports = {',
        "  name: './fake-target',",
        "  runnerTargets: ['buildtool'],",
        '  rules() {',
        '    return [{',
        "      matches: ({ name }) => name === 'buildtool',",
        "      reason: (_r, { args }) => (args[0] === 'test' ?",
        "        { tool: 'buildtool', verb: 'test', text: 'buildtool test' } : null),",
        '    }];',
        '  },',
        '};',
      ].join('\n'),
    );
    const configPath = path.join(tmpDir, 'job-queue.config.json');
    fs.writeFileSync(
      configPath,
      JSON.stringify({
        version: 1,
        entry: 'scripts/human/job-queue.js',
        plugins: [{ name: 'nvidia-gpu' }, { name: './fake-target' }],
      }),
    );
    return fn(fs.realpathSync.native(configPath));
  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

function checks(assert, { createEngine }) {
  assert.deepStrictEqual(
    nvidiaPlugin.runners,
    ['compute-sanitizer', 'cuda-memcheck', 'nsys', 'ncu'],
    'nvidia-gpu runners list',
  );
  withNeutralTargetConfig((configPath) => {
    const { check } = createEngine({ configPath, tolerant: false });
    assert.deepStrictEqual(check('compute-sanitizer --tool memcheck buildtool test'), ['buildtool test']);
    assert.deepStrictEqual(check('cuda-memcheck buildtool test'), ['buildtool test']);
    assert.deepStrictEqual(check('nsys profile buildtool test'), ['buildtool test']);
    assert.deepStrictEqual(check('ncu buildtool test'), ['buildtool test']);

    assert.deepStrictEqual(check('cuda-memcheck --help'), []);
    assert.deepStrictEqual(check('nsys --version'), []);
    assert.deepStrictEqual(check('ncu --help'), []);
    assert.deepStrictEqual(check('compute-sanitizer --version'), []);
    assert.deepStrictEqual(check('compute-sanitizer other-tool'), []);
  });
}

module.exports = { checks };
