'use strict';

// The rewrite of check-queued-command.js: a plain build or test command that the hook would deny in Claude Code is
// handed back wrapped in the job queue (updatedInput of the PreToolUse hook) instead of being denied:
//   dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda --no-build
//   -> node scripts/human/job-queue.js run --gpu --shell bash -- 'dotnet test tests/Compiler/Kassyi.GSharp.Tests.Cuda
//      --no-build' (on one line)
//
// What is rewritten: segments joined by && only, each either a command the hook allows (cd x, git ...) or a bare
// dotnet build / test / publish / pack / vstest / msbuild, msbuild or just <recipe> (environment assignments before it
// allowed in bash), with a trailing comment kept outside the quotes (# intent: ...). Everything else stays denied:
// other separators (; | || & line breaks), heredocs and here-strings, substitutions and bash backticks, input
// redirections, groups, assignments, launch prefixes, wrappers (cmd /c, pwsh -c, bash -c, wsl, Start-Process), the
// long test scripts and gpu-lock.js run directly, dotnet watch and Stryker, a just recipe after a change of directory,
// PowerShell's --%, a command that Git Bash would turn into a Windows path (/x..., X=/x...), a segment that already
// runs the queue, and a working directory without scripts/human/job-queue.js.
//
// --gpu, from data that already exists in the repository:
//   - dotnet build / publish / pack / msbuild: no GPU, unless an MSBuild target named like a test target runs.
//   - dotnet test / vstest: the test projects the command names (tests/<category>/<name>/<name>.csproj, matched by
//     name); every one sets <GSharpTargetDialect>Cpu: no --gpu; any sets Cuda: --gpu; a project that says nothing of
//     its dialect, or no project named at all: undecided (the same rule as cpuOnlyTestProjects of
//     scripts/human/job-queue/machine.ts, which this CommonJS script cannot import).
//   - just <recipe>: the options the recipe itself passes to {{queue}} in the justfile (--gpu, --weight <n>); a recipe
//     that picks them from its arguments, or does not queue itself, is undecided.
// An undecided command is denied as before, with a sentence saying that it may need --gpu.
//
// Usage: require('./queue-rewrite.js').planRewrite(command, dialect, cwd) returns { command, gpu } for a rewrite,
// { note } for a denial with a sentence to add, or null for the plain denial; queuedText(rewritten, dialect) returns
// the command a rewritten command queues.

const fs = require('fs');
const path = require('path');
const { check } = require('./command-rules.js');
const {
  withDialect,
  splitSegments,
  words,
  unquote,
  argumentText,
  programName,
  substitutions,
} = require('./shell-reader.js');

const QueueScript = 'scripts/human/job-queue.js';
const DirectoryChanges = ['cd', 'pushd', 'popd', 'chdir', 'set-location', 'sl', 'push-location', 'pop-location'];

/** The text quoted as one single-quoted word of the dialect. */
function quote(kind, text) {
  return kind === 'pwsh' ? `'${text.replace(/'/g, "''")}'` : `'${text.replace(/'/g, "'\\''")}'`;
}

/** The command that queues `command`: node scripts/human/job-queue.js run [options] --shell <s> -- '<command>'. */
function queuedCommand(kind, command, options, comment) {
  const shell = kind === 'pwsh' ? 'pwsh' : 'bash';
  const head = ['node', QueueScript, 'run', ...options, '--shell', shell, '--'].join(' ');
  return `${head} ${quote(kind, command)}${comment === '' ? '' : ` ${comment}`}`;
}

/** The command a queued command runs: the word after `--`, unquoted as its shell does. */
function queuedText(rewritten, kind) {
  return withDialect(kind, () => {
    const list = words(rewritten);
    const index = list.indexOf('--');
    return index < 0 || index + 1 >= list.length ? '' : argumentText(list[index + 1]);
  });
}

/** True when a word outside quotes holds <: an input redirection or a process substitution. */
function hasInputRedirection(segment) {
  return /</.test(segment.replace(/"(?:[^"\\`]|[\\`].)*"|'[^']*'/g, ''));
}

/** The names of the test projects under tests/<category>/<name>/<name>.csproj. */
function testProjects(root) {
  const testsDir = path.join(root, 'tests');
  const names = [];
  let categories = [];
  try {
    categories = fs.readdirSync(testsDir, { withFileTypes: true }).filter((entry) => entry.isDirectory());
  } catch (error) {
    return { names, error: `could not read ${testsDir}: ${error.message}` };
  }
  for (const category of categories) {
    for (const entry of fs.readdirSync(path.join(testsDir, category.name), { withFileTypes: true })) {
      const project = path.join(testsDir, category.name, entry.name, `${entry.name}.csproj`);
      if (entry.isDirectory() && fs.existsSync(project)) {
        names.push({ name: entry.name, project });
      }
    }
  }
  return { names, error: '' };
}

/** The test projects named in the text: the name not followed by more of a name (.csproj and .dll do not count). */
function namedProjects(text, projects) {
  return projects.filter(({ name }) => {
    const escaped = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return new RegExp(`(^|[^\\w.])${escaped}(?=$|[^\\w.]|\\.(csproj|dll)\\b)`, 'i').test(text);
  });
}

/** GPU use of a dotnet test / vstest segment: { gpu } or { undecided: why }. */
function testGpu(segment, root) {
  const { names, error } = testProjects(root);
  if (error !== '') {
    return { undecided: error };
  }
  const named = namedProjects(segment, names);
  if (named.length === 0) {
    return { undecided: 'it names no test project of tests/' };
  }
  let gpu = false;
  for (const { name, project } of named) {
    const dialect = /<GSharpTargetDialect>\s*(\w+)\s*<\/GSharpTargetDialect>/i.exec(fs.readFileSync(project, 'utf8'));
    if (dialect === null || !/^(cpu|cuda)$/i.test(dialect[1])) {
      return { undecided: `${name}.csproj sets no GSharpTargetDialect of Cpu or Cuda` };
    }
    gpu = gpu || /^cuda$/i.test(dialect[1]);
  }
  return { gpu };
}

/** The bodies of the recipe in the justfile (one per platform variant), continued lines joined; null when absent. */
function recipeBodies(justfile, recipe) {
  const lines = justfile.split(/\r?\n/);
  const bodies = [];
  // Every regular-expression character of the name escaped, backslash included (only - and . were before)
  const header = new RegExp(`^@?${recipe.replace(/[\\^$.*+?()[\]{}|-]/g, '\\$&')}(\\s[^\\n]*)?:\\s*$`);
  for (let i = 0; i < lines.length; i++) {
    if (!header.test(lines[i]) || /:=/.test(lines[i])) {
      continue;
    }
    const body = [];
    for (let j = i + 1; j < lines.length && (lines[j] === '' || /^\s/.test(lines[j])); j++) {
      body.push(lines[j]);
    }
    bodies.push(body.join('\n').replace(/\\\n\s*/g, ' '));
  }
  return bodies;
}

/** The queue options of a just recipe: { options, gpu } or { undecided: why }. */
function recipeQueue(recipe, root) {
  let justfile;
  try {
    justfile = fs.readFileSync(path.join(root, 'justfile'), 'utf8');
  } catch (error) {
    return { undecided: `could not read the justfile: ${error.message}` };
  }
  if (!/^queue := "node scripts\/human\/job-queue\.js run"\s*$/m.test(justfile)) {
    return { undecided: 'the justfile defines no queue variable' };
  }
  const found = new Set();
  for (const body of recipeBodies(justfile, recipe)) {
    const pattern = /\{\{queue\}\}(.*?)\s--\s/g;
    let match;
    while ((match = pattern.exec(body)) !== null) {
      found.add(match[1].trim().replace(/\s+/g, ' '));
    }
  }
  if (found.size === 0) {
    return { undecided: `just ${recipe} does not queue itself` };
  }
  const [options] = [...found];
  if (found.size > 1 || !/^((--gpu|--weight \d+)( |$))*$/.test(options)) {
    return { undecided: `just ${recipe} chooses its queue options from its arguments` };
  }
  const list = options === '' ? [] : options.split(' ');
  return { options: list.filter((option) => option !== '--gpu'), gpu: list.includes('--gpu') };
}

/** True for an MSBuild target option naming a test target: -t:VSTest, /target:Test. */
function runsTestTarget(list) {
  return list.some((word) => /^[-/]t(arget)?:.*test/i.test(word));
}

/** One denied segment: { gpu, options } or { undecided } or null (plain denial). */
function deniedSegment(segment, list, reason, root, changedDirectory) {
  if (!/^(dotnet|just|msbuild)(\.exe)?$/i.test(list[0])) {
    return null; // a quoted path, a wrapper, a launch prefix, a script run directly
  }
  if (/^(dotnet (build|publish|pack|msbuild)|msbuild)$/.test(reason)) {
    return runsTestTarget(list)
      ? { undecided: 'it runs an MSBuild target named like a test target' }
      : { gpu: false, options: [] };
  }
  if (/^dotnet (test|vstest)$/.test(reason)) {
    const result = testGpu(segment, root);
    return result.undecided === undefined ? { gpu: result.gpu, options: [] } : result;
  }
  if (/^just /.test(reason) && !changedDirectory) {
    return recipeQueue(reason.slice('just '.length), root);
  }
  return null; // dotnet watch, Stryker, just after cd
}

/**
 * The rewrite of a command the hook denies, run by the Claude Code tool of `kind` ('bash' or 'pwsh') in `cwd`:
 * { command, gpu } to queue it, { note } to deny it with that sentence added, or null to deny it as before.
 */
function planRewrite(command, kind, cwd) {
  const text = command.trim();
  if (text === '' || !fs.existsSync(path.join(cwd, QueueScript))) {
    return null;
  }
  const found = [];
  withDialect(kind, () => splitSegments(text, found));
  const comments = found.filter((item) => item.kind === 'comment');
  if (found.some((item) => item.kind !== '&&' && item.kind !== 'comment') || comments.length > 1) {
    return null;
  }
  // A trailing comment (# intent: ...) stays outside the quotes, where the command gate reads it
  const body = comments.length === 0 ? text : text.slice(0, comments[0].index).trimEnd();
  const comment = comments.length === 0 ? '' : text.slice(comments[0].index);
  if (
    (kind === 'bash' && (/`/.test(body) || /^([^\s=]*=)?\//.test(body))) ||
    (kind === 'pwsh' && /--%/.test(body))
  ) {
    return null; // a bash backtick substitution; a path Git Bash converts; PowerShell's stop-parsing token
  }
  const segments = withDialect(kind, () => splitSegments(body));
  let gpu = false;
  let options = [];
  let changedDirectory = false;
  const undecided = [];
  for (const raw of segments) {
    const segment = raw.trim();
    if (segment === '' || /^[({@$&.!]/.test(segment) || hasInputRedirection(segment)) {
      return null;
    }
    if (withDialect(kind, () => substitutions(segment)).length > 0) {
      return null;
    }
    let list = withDialect(kind, () => words(segment));
    while (kind === 'bash' && list.length > 1 && /^[A-Za-z_]\w*=/.test(list[0])) {
      list = list.slice(1); // FOO=1 dotnet test: the assignment travels with the command
    }
    const name = programName(list[0] || '');
    const reasons = check(segment, kind);
    if (reasons.length === 0) {
      if (name === 'node' && /(^|[\\/])(job|build)-queue\.js$/.test(unquote(list[1] || ''))) {
        return null; // already queued: nesting would run it at once in this job
      }
      changedDirectory = changedDirectory || DirectoryChanges.includes(name);
      continue;
    }
    if (reasons.length > 1) {
      return null;
    }
    const result = deniedSegment(segment, list, reasons[0], cwd, changedDirectory);
    if (result === null) {
      return null;
    }
    if (result.undecided !== undefined) {
      undecided.push(`${reasons[0]}: ${result.undecided}`);
      continue;
    }
    gpu = gpu || result.gpu;
    if (result.options.length > options.length) {
      options = result.options; // --weight <n> of a just recipe
    }
  }
  if (undecided.length > 0) {
    return {
      note:
        `The hook did not queue it by itself, since it cannot tell whether the command uses the GPU ` +
        `(${undecided.join('; ')}): add --gpu when it does.`,
    };
  }
  return { command: queuedCommand(kind, body, gpu ? ['--gpu', ...options] : options, comment), gpu, queued: body };
}

module.exports = { planRewrite, queuedText, quote };
