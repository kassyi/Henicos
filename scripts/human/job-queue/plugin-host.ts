// Client- and view-side plugin host: discovers and dynamically imports plugin.ts modules, composes synchronous
// hooks (first, first-per-field, sequential, all), isolates exceptions, validates return values, and warns on slow
// hooks. Never loaded by the scheduler.

import * as fs from "node:fs";
import * as path from "node:path";
import { pathToFileURL } from "node:url";
import type { ReadStep } from "./command-reader.ts";
import { loadConfig, type QueueConfig } from "./config.ts";
import { EntryScript } from "./constants.ts";
import {
  PluginApiVersion,
  type AssertModule,
  type CheckResult,
  type Classification,
  type ClientContext,
  type GrantInfo,
  type PreparedCommand,
  type QueuePlugin,
  type ResourceRequest,
  type ShareAnswer,
  type SplitPlan,
  type StepContext,
  type StepKeys,
  type ViewPanel,
} from "./plugin-api.ts";
import { errorMessage } from "./history.ts";
import { isCommandKeys, isMemoryMb } from "./protocol.ts";

export type BaseClientContext = Omit<ClientContext, "options">;

const DefaultImportTimeoutMs = 5000;
const DefaultSlowHookMs = 500;
/** How long one plugin may take to answer checkShare (the answer may run a test list through the queue). */
export const CheckShareTimeoutMs = 180_000;
/** How long the `stillValid` of a shared answer may take (it should only compare what was measured). */
export const StillValidTimeoutMs = 30_000;
export const DefaultPluginsRoot = path.resolve(import.meta.dirname, "plugins");

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function isValidSplitPlan(value: unknown): value is SplitPlan {
  if (!isPlainObject(value)) {
    return false;
  }
  return (
    typeof value.before === "string" &&
    value.before.length > 0 &&
    typeof value.after === "string" &&
    value.after.length > 0 &&
    typeof value.beforeRole === "string" &&
    value.beforeRole.length > 0 &&
    typeof value.afterRole === "string" &&
    value.afterRole.length > 0
  );
}

function isValidViewPanel(value: unknown): value is ViewPanel {
  if (!isPlainObject(value)) {
    return false;
  }
  return (
    typeof value.read === "function" &&
    typeof value.render === "function" &&
    typeof value.summary === "function" &&
    typeof value.help === "function"
  );
}

function resolvePluginFolder(name: string, configRoot: string, pluginsRoot = DefaultPluginsRoot): string | null {
  if (name.startsWith("./") || name.startsWith(".\\")) {
    return path.resolve(configRoot, name);
  }
  if (name.includes("/") || name.includes("\\") || name.includes("..")) {
    return null;
  }
  const resolved = path.resolve(pluginsRoot, name);
  const relative = path.relative(pluginsRoot, resolved);
  if (relative === "" || relative.startsWith("..") || path.isAbsolute(relative)) {
    return null;
  }
  return resolved;
}

async function importWithTimeout(fileUrl: string, timeoutMs: number): Promise<unknown> {
  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<never>((_resolve, reject) => {
    timer = setTimeout(() => reject(new Error(`timed out after ${timeoutMs} ms`)), timeoutMs);
    timer.unref?.();
  });
  try {
    return await Promise.race([import(fileUrl), timeout]);
  } finally {
    if (timer !== undefined) {
      clearTimeout(timer);
    }
  }
}

export interface PluginHostLoadOptions {
  importTimeoutMs?: number;
  slowHookMs?: number;
}

export class PluginHost {
  readonly plugins: readonly QueuePlugin[];
  private readonly pluginOptions: readonly Readonly<Record<string, unknown>>[];
  readonly configRoot: string;
  private readonly warn: (message: string) => void;
  private readonly slowHookMs: number;
  private readonly warnedFailures = new Set<string>();
  /** The time limit of one checkShare call; a field so that a self-test can shorten it. */
  checkShareTimeoutMs = CheckShareTimeoutMs;
  /** The time limit of one `stillValid` call; a field so that a self-test can shorten it. */
  stillValidTimeoutMs = StillValidTimeoutMs;

  constructor(
    plugins: readonly QueuePlugin[],
    pluginOptions: readonly (Readonly<Record<string, unknown>> | undefined)[],
    configRoot: string,
    warn: (message: string) => void = (message) => process.stderr.write(`${message}\n`),
    slowHookMs = DefaultSlowHookMs,
  ) {
    this.plugins = plugins;
    this.pluginOptions = plugins.map((_, i) => pluginOptions[i] ?? {});
    this.configRoot = configRoot;
    this.warn = warn;
    this.slowHookMs = slowHookMs;
  }

  /**
   * Loads plugins listed in `config.plugins` unless `JOB_QUEUE_PLUGINS=0` is set.
   * Invalid, missing, or timing-out plugins emit a warning and are skipped.
   */
  static async load(
    config: QueueConfig,
    configRoot: string,
    warn: (message: string) => void = (message) => process.stderr.write(`${message}\n`),
    env: Readonly<NodeJS.ProcessEnv> = process.env,
    loadOptions: PluginHostLoadOptions = {},
  ): Promise<PluginHost> {
    const slowHookMs = loadOptions.slowHookMs ?? DefaultSlowHookMs;
    if (env.JOB_QUEUE_PLUGINS === "0") {
      return new PluginHost([], [], configRoot, warn, slowHookMs);
    }
    const timeoutMs = loadOptions.importTimeoutMs ?? DefaultImportTimeoutMs;
    const loaded: QueuePlugin[] = [];
    const optionsList: Readonly<Record<string, unknown>>[] = [];

    for (const entry of config.plugins) {
      const folder = resolvePluginFolder(entry.name, configRoot);
      if (folder === null) {
        warn(`[job-queue] plugin ${entry.name} has an invalid bare name; skipping`);
        continue;
      }
      if (!fs.existsSync(folder)) {
        warn(`[job-queue] plugin ${entry.name} folder not found (${folder}); skipping`);
        continue;
      }
      const pluginPath = path.join(folder, "plugin.ts");
      if (!fs.existsSync(pluginPath)) {
        // Folders like nvidia-gpu provide CJS helpers (holder-watch.js) without a TS client plugin
        continue;
      }
      let realPath: string;
      try {
        realPath = fs.realpathSync.native(pluginPath);
      } catch (error) {
        warn(`[job-queue] plugin ${entry.name} could not be resolved: ${errorMessage(error)}; skipping`);
        continue;
      }
      let moduleValue: unknown;
      try {
        moduleValue = await importWithTimeout(pathToFileURL(realPath).href, timeoutMs);
      } catch (error) {
        warn(`[job-queue] plugin ${entry.name} failed to load: ${errorMessage(error)}; skipping`);
        continue;
      }
      const exported = isPlainObject(moduleValue) ? moduleValue.default : undefined;
      if (!isPlainObject(exported)) {
        warn(`[job-queue] plugin ${entry.name} has no default export object; skipping`);
        continue;
      }
      if (exported.name !== entry.name) {
        warn(`[job-queue] plugin ${entry.name} exported name "${String(exported.name)}"; skipping`);
        continue;
      }
      if (exported.apiVersion !== PluginApiVersion) {
        warn(
          `[job-queue] plugin ${entry.name} apiVersion ${String(exported.apiVersion)}` +
            ` !== ${PluginApiVersion}; skipping`,
        );
        continue;
      }
      loaded.push(exported as unknown as QueuePlugin);
      optionsList.push(entry.options ?? {});
    }

    return new PluginHost(loaded, optionsList, configRoot, warn, slowHookMs);
  }

  private contextFor(index: number, context: BaseClientContext): ClientContext {
    return {
      ...context,
      options: this.pluginOptions[index] ?? {},
    };
  }

  private callHook<T>(pluginIndex: number, hookName: string, invoke: (plugin: QueuePlugin) => T): T | undefined {
    const plugin = this.plugins[pluginIndex] as QueuePlugin;
    const started = performance.now();
    try {
      const result = invoke(plugin);
      const elapsed = performance.now() - started;
      if (elapsed > this.slowHookMs) {
        this.warn(`[job-queue] plugin ${plugin.name}.${hookName} took ${Math.round(elapsed)} ms`);
      }
      return result;
    } catch (error) {
      const elapsed = performance.now() - started;
      if (elapsed > this.slowHookMs) {
        this.warn(`[job-queue] plugin ${plugin.name}.${hookName} took ${Math.round(elapsed)} ms`);
      }
      const key = `${pluginIndex}\0${plugin.name}\0${hookName}`;
      if (!this.warnedFailures.has(key)) {
        this.warnedFailures.add(key);
        const message = errorMessage(error);
        this.warn(`[job-queue] plugin ${plugin.name}.${hookName} failed: ${message}; continuing without it`);
      }
      return undefined;
    }
  }

  /** [all] Calls every plugin in order, collecting hints, stopping at the first refuse. */
  check(command: string, resource: ResourceRequest | null, context: BaseClientContext): CheckResult {
    const hints: string[] = [];
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.check !== "function") {
        continue;
      }
      const res = this.callHook(i, "check", (p) => p.check?.(command, resource, this.contextFor(i, context)));
      if (!isPlainObject(res)) {
        continue;
      }
      if (Array.isArray(res.hints)) {
        for (const hint of res.hints) {
          if (typeof hint === "string" && hint.length > 0) {
            hints.push(hint);
          }
        }
      }
      if (typeof res.refuse === "string" && res.refuse.length > 0) {
        return hints.length > 0 ? { refuse: res.refuse, hints } : { refuse: res.refuse };
      }
    }
    return hints.length > 0 ? { hints } : {};
  }

  /** [first] Returns the first valid SplitPlan. */
  splitBeforeTurn(command: string, resource: ResourceRequest, context: BaseClientContext): SplitPlan | undefined {
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.splitBeforeTurn !== "function") {
        continue;
      }
      const res = this.callHook(i, "splitBeforeTurn", (p) =>
        p.splitBeforeTurn?.(command, resource, this.contextFor(i, context)),
      );
      if (isValidSplitPlan(res)) {
        return {
          before: res.before,
          after: res.after,
          beforeRole: res.beforeRole,
          afterRole: res.afterRole,
        };
      }
    }
    return undefined;
  }

  /** [first per-field] Calls all plugins in order; each field takes the first valid defined value. */
  classify(command: string, resource: ResourceRequest | null, context: BaseClientContext): Classification {
    const result: Classification = {};
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.classify !== "function") {
        continue;
      }
      const res = this.callHook(i, "classify", (p) => p.classify?.(command, resource, this.contextFor(i, context)));
      if (!isPlainObject(res)) {
        continue;
      }
      if (result.test === undefined && typeof res.test === "boolean") {
        result.test = res.test;
      }
      if (result.memoryMb === undefined && isMemoryMb(res.memoryMb)) {
        result.memoryMb = res.memoryMb;
      }
      if (result.share === undefined && (res.share === "shared" || res.share === "exclusive")) {
        result.share = res.share;
      }
    }
    if (resource?.forcedExclusive) {
      result.share = "exclusive";
    }
    return result;
  }

  /**
   * [first, async] Asks the plugins in order for the share of a GPU turn that classify did not share: the first
   * "shared" or "exclusive" wins and undefined passes on. A throw, a time out (checkShareTimeoutMs) or any other value
   * is "exclusive" and ends the asking, with one line saying why. Undefined when no plugin answers.
   */
  async checkShare(command: string, context: BaseClientContext): Promise<ShareAnswer | undefined> {
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.checkShare !== "function") {
        continue;
      }
      let timer: ReturnType<typeof setTimeout> | undefined;
      try {
        const limit = new Promise<never>((_, reject) => {
          timer = setTimeout(
            () => reject(new Error(`timed out after ${this.checkShareTimeoutMs} ms`)),
            this.checkShareTimeoutMs,
          );
        });
        const res = await Promise.race([plugin.checkShare(command, this.contextFor(i, context)), limit]);
        if (res === undefined) {
          continue;
        }
        if (res === "shared" || res === "exclusive") {
          return { share: res };
        }
        if (isPlainObject(res) && (res.share === "shared" || res.share === "exclusive")) {
          // An exclusive answer needs no re-check; a stillValid that is no function is ignored (the client then has none)
          const stillValid =
            res.share === "shared" && typeof res.stillValid === "function"
              ? (res.stillValid as () => Promise<boolean>)
              : undefined;
          return stillValid !== undefined ? { share: "shared", stillValid } : { share: res.share };
        }
        this.warn(`[job-queue] plugin ${plugin.name}.checkShare returned an invalid value; taking the GPU exclusively`);
        return { share: "exclusive" };
      } catch (error) {
        this.warn(
          `[job-queue] plugin ${plugin.name}.checkShare failed: ${errorMessage(error)}; taking the GPU exclusively`,
        );
        return { share: "exclusive" };
      } finally {
        clearTimeout(timer);
      }
    }
    return undefined;
  }

  /**
   * Runs the `stillValid` of a shared answer: true only when it resolves to exactly true within stillValidTimeoutMs.
   * A throw, a time out or any other value is false, with one line saying why.
   */
  async stillValid(answer: ShareAnswer): Promise<boolean> {
    if (answer.stillValid === undefined) {
      return true;
    }
    let timer: ReturnType<typeof setTimeout> | undefined;
    try {
      const limit = new Promise<never>((_, reject) => {
        timer = setTimeout(
          () => reject(new Error(`timed out after ${this.stillValidTimeoutMs} ms`)),
          this.stillValidTimeoutMs,
        );
      });
      const valid = await Promise.race([answer.stillValid(), limit]);
      if (valid !== true) {
        this.warn("[job-queue] the check that the shared GPU turn still holds says no");
      }
      return valid === true;
    } catch (error) {
      this.warn(`[job-queue] the check that the shared GPU turn still holds failed: ${errorMessage(error)}`);
      return false;
    } finally {
      clearTimeout(timer);
    }
  }

  /** [first] Returns the first valid StepKeys. */
  stepKeys(step: ReadStep, context: StepContext): StepKeys | undefined {
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.stepKeys !== "function") {
        continue;
      }
      const res = this.callHook(i, "stepKeys", (p) => p.stepKeys?.(step, context));
      if (isCommandKeys(res)) {
        return { normalized: res.normalized, shape: res.shape, kind: res.kind };
      }
    }
    return undefined;
  }

  /** [first] Returns the first non-empty session identifier. */
  session(context: BaseClientContext): string | undefined {
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.session !== "function") {
        continue;
      }
      const res = this.callHook(i, "session", (p) => p.session?.(this.contextFor(i, context)));
      if (typeof res === "string" && res.length > 0) {
        return res;
      }
    }
    return undefined;
  }

  /** [sequential] Chains command rewrites, merges env later-wins, and joins non-empty notes with ", ". */
  prepareCommand(
    command: string,
    grant: GrantInfo,
    context: BaseClientContext,
  ): { command: string; env: Record<string, string>; note?: string } {
    let currentCommand = command;
    // Without a prototype, so that an env key named "__proto__" stays an own data property
    const env = Object.create(null) as Record<string, string>;
    const notes: string[] = [];
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.prepareCommand !== "function") {
        continue;
      }
      const res: PreparedCommand | undefined = this.callHook(i, "prepareCommand", (p) =>
        p.prepareCommand?.(currentCommand, grant, this.contextFor(i, context)),
      );
      if (!isPlainObject(res)) {
        continue;
      }
      if (typeof res.command === "string" && res.command.length > 0) {
        currentCommand = res.command;
      }
      if (isPlainObject(res.env)) {
        for (const [key, value] of Object.entries(res.env)) {
          if (typeof value === "string") {
            env[key] = value;
          }
        }
      }
      if (typeof res.note === "string" && res.note.trim() !== "") {
        notes.push(res.note);
      }
    }
    return notes.length > 0
      ? { command: currentCommand, env, note: notes.join(", ") }
      : { command: currentCommand, env };
  }

  /** [sequential] Chains short command labels across plugins. */
  shortName(command: string, short: string): string {
    let current = short;
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.shortName !== "function") {
        continue;
      }
      const res = this.callHook(i, "shortName", (p) => p.shortName?.(command, current));
      if (typeof res === "string" && res.length > 0) {
        current = res;
      }
    }
    return current;
  }

  /** [first] Returns the first valid ViewPanel. */
  panel(context: BaseClientContext): ViewPanel | undefined {
    for (let i = 0; i < this.plugins.length; i++) {
      const plugin = this.plugins[i] as QueuePlugin;
      if (typeof plugin.panel !== "function") {
        continue;
      }
      const res = this.callHook(i, "panel", (p) => p.panel?.(this.contextFor(i, context)));
      if (isValidViewPanel(res)) {
        return res;
      }
    }
    return undefined;
  }
}

export interface PluginChecksOptions {
  config?: QueueConfig;
  configRoot?: string;
  pluginsRoot?: string;
  importTimeoutMs?: number;
}

function bundledPluginOrder(name: string): number {
  return name === "dotnet" ? 0 : name === "just" ? 1 : 2;
}

/**
 * Discovers and runs `checks(assert)` for all bundled `plugins/<name>/checks.ts` (dotnet, just, then sorted others)
 * plus `config.plugins[]` entries, regardless of `JOB_QUEUE_PLUGINS=0`. Rejects if an enabled config plugin
 * lacks `checks.ts` or if any `checks(assert)` throws.
 */
export async function runPluginChecks(assert: AssertModule, options: PluginChecksOptions = {}): Promise<void> {
  const pluginsRoot = options.pluginsRoot ?? DefaultPluginsRoot;
  const timeoutMs = options.importTimeoutMs ?? DefaultImportTimeoutMs;
  let config = options.config;
  let configRoot = options.configRoot;
  if (config === undefined) {
    const loaded =
      configRoot !== undefined
        ? loadConfig({
            mode: "status",
            configPath: path.join(configRoot, "job-queue.config.json"),
            fromDirectory: configRoot,
            warn: () => {},
          })
        : loadConfig({
            mode: "status",
            fromDirectory: path.dirname(EntryScript),
          });
    config = loaded.config;
    configRoot = loaded.configRoot;
  } else if (configRoot === undefined) {
    configRoot = path.dirname(EntryScript);
  }

  const targets: { name: string; checksPath: string }[] = [];
  const seen = new Set<string>();
  const addTarget = (name: string, checksPath: string): void => {
    const realPath = fs.realpathSync.native(checksPath);
    if (seen.has(realPath)) {
      return;
    }
    seen.add(realPath);
    targets.push({ name, checksPath: realPath });
  };

  if (fs.existsSync(pluginsRoot)) {
    const bundledNames = fs
      .readdirSync(pluginsRoot, { withFileTypes: true })
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name)
      .sort((a, b) => bundledPluginOrder(a) - bundledPluginOrder(b) || (a < b ? -1 : a > b ? 1 : 0));
    for (const name of bundledNames) {
      const folder = resolvePluginFolder(name, configRoot, pluginsRoot);
      if (folder === null) {
        continue;
      }
      const checksPath = path.join(folder, "checks.ts");
      if (fs.existsSync(checksPath)) {
        addTarget(name, checksPath);
      }
    }
  }

  for (const entry of config.plugins) {
    const folder = resolvePluginFolder(entry.name, configRoot, pluginsRoot);
    if (folder === null) {
      throw new Error(`plugin ${entry.name} has an invalid bare name`);
    }
    const checksPath = path.join(folder, "checks.ts");
    if (!fs.existsSync(checksPath)) {
      throw new Error(`plugin ${entry.name} has no checks.ts (${checksPath})`);
    }
    addTarget(entry.name, checksPath);
  }

  for (const target of targets) {
    try {
      const moduleValue = await importWithTimeout(pathToFileURL(target.checksPath).href, timeoutMs);
      const checks = isPlainObject(moduleValue) ? moduleValue.checks : undefined;
      if (typeof checks !== "function") {
        throw new Error("missing exported checks(assert) function");
      }
      await (checks as (assert: AssertModule) => void | Promise<void>)(assert);
    } catch (error) {
      throw new Error(`plugin ${target.name} checks failed: ${errorMessage(error)}`, { cause: error });
    }
  }
}
