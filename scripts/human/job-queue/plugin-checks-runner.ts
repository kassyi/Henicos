// Discovery and execution of plugin `checks.ts` suites for `job-queue.js --self-test`.

import * as fs from "node:fs";
import * as path from "node:path";
import { pathToFileURL } from "node:url";
import { loadConfig, type QueueConfig } from "./config.ts";
import { EntryScript } from "./constants.ts";
import { errorMessage } from "./history.ts";
import type { AssertModule } from "./plugin-api.ts";

export const DefaultImportTimeoutMs = 5000;
export const DefaultPluginsRoot = path.resolve(import.meta.dirname, "plugins");

export function isPlainObject(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

export function resolvePluginFolder(name: string, configRoot: string, pluginsRoot = DefaultPluginsRoot): string | null {
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

export async function importWithTimeout(fileUrl: string, timeoutMs: number): Promise<unknown> {
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

export interface PluginChecksOptions {
  config?: QueueConfig;
  configRoot?: string;
  pluginsRoot?: string;
  importTimeoutMs?: number;
}

function bundledPluginOrder(name: string): number {
  return name === "dotnet" ? 0 : name === "just" ? 1 : 2;
}

function resolveChecksConfig(options: PluginChecksOptions): { config: QueueConfig; configRoot: string } {
  if (options.config !== undefined) {
    return { config: options.config, configRoot: options.configRoot ?? path.dirname(EntryScript) };
  }
  const loaded =
    options.configRoot !== undefined
      ? loadConfig({
          mode: "status",
          configPath: path.join(options.configRoot, "job-queue.config.json"),
          fromDirectory: options.configRoot,
          warn: () => {},
        })
      : loadConfig({
          mode: "status",
          fromDirectory: path.dirname(EntryScript),
        });
  return { config: loaded.config, configRoot: loaded.configRoot };
}

function collectCheckTargets(
  config: QueueConfig,
  configRoot: string,
  pluginsRoot: string,
): { name: string; checksPath: string }[] {
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
  return targets;
}

/**
 * Discovers and runs `checks(assert)` for all bundled `plugins/<name>/checks.ts` (dotnet, just, then sorted others)
 * plus `config.plugins[]` entries, regardless of `JOB_QUEUE_PLUGINS=0`. Rejects if an enabled config plugin
 * lacks `checks.ts` or if any `checks(assert)` throws.
 */
export async function runPluginChecks(assert: AssertModule, options: PluginChecksOptions = {}): Promise<void> {
  const pluginsRoot = options.pluginsRoot ?? DefaultPluginsRoot;
  const timeoutMs = options.importTimeoutMs ?? DefaultImportTimeoutMs;
  const { config, configRoot } = resolveChecksConfig(options);
  const targets = collectCheckTargets(config, configRoot, pluginsRoot);

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
