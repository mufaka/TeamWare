import type { AgentConfig, McpServerConfig, RepositoryConfig } from "../config.js";
import { DEFAULTS, effective } from "../config.js";
import { TeamWareMcpClient } from "../mcp/client.js";
import type { AgentProfileConfiguration } from "../mcp/types.js";
import { StatusTransitionHandler } from "./transitions.js";
import { processTask } from "../processing/processor.js";
import type { EffectiveConfig } from "../processing/processor.js";
import { resolveRepository, ensureRepository } from "../repository/manager.js";

/**
 * Main polling loop. Mirrors AgentPollingLoop from the .NET agent.
 *
 * On each cycle:
 * 1. get_my_profile — check kill switch, merge server config
 * 2. my_assignments — filter to ToDo
 * 3. Process each task sequentially
 */
export class PollingLoop {
  private readonly config: AgentConfig;
  private readonly mcp: TeamWareMcpClient;
  private readonly transitions: StatusTransitionHandler;
  private running = false;

  constructor(config: AgentConfig) {
    this.config = config;
    this.mcp = new TeamWareMcpClient({
      mcpUrl: config.teamware.mcpUrl,
      personalAccessToken: config.teamware.personalAccessToken,
    });
    this.transitions = new StatusTransitionHandler(this.mcp);
  }

  async start(signal?: AbortSignal): Promise<void> {
    this.running = true;
    await this.mcp.connect();

    console.log("[agent] Connected to TeamWare MCP. Starting polling loop.");

    try {
      while (this.running && !signal?.aborted) {
        let intervalSeconds: number = DEFAULTS.pollingIntervalSeconds;

        try {
          intervalSeconds = await this.executeCycle();
        } catch (err) {
          console.error("[agent] Polling cycle error:", err);
        }

        await this.sleep(intervalSeconds * 1000, signal);
      }
    } finally {
      await this.mcp.disconnect();
      console.log("[agent] Disconnected from TeamWare MCP.");
    }
  }

  stop(): void {
    this.running = false;
  }

  /** Executes one polling cycle. Returns the polling interval for the next cycle. */
  private async executeCycle(): Promise<number> {
    // 1. Get profile and merge server config
    const profile = await this.mcp.getMyProfile();
    const serverConfig = profile.configuration;

    if (!profile.isAgentActive) {
      console.log("[agent] Agent is paused (isAgentActive=false). Skipping cycle.");
      return this.resolvePollingInterval(serverConfig);
    }

    const merged = this.mergeConfig(serverConfig);

    // 2. Get assignments, filter to ToDo
    const assignments = await this.mcp.getMyAssignments();
    const todoTasks = assignments.filter((t) => t.status === "ToDo");

    if (todoTasks.length === 0) {
      console.log("[agent] No ToDo tasks found.");
      return merged.pollingIntervalSeconds;
    }

    console.log(`[agent] Found ${todoTasks.length} ToDo task(s).`);

    // 3. Process each task sequentially
    for (const task of todoTasks) {
      if (!this.running) break;

      try {
        await this.processOneTask(task.id, task.title, task.projectName, task.projectId, merged);
      } catch (err) {
        console.error(`[agent] Failed to process task #${task.id}:`, err);
      }
    }

    return merged.pollingIntervalSeconds;
  }

  private async processOneTask(
    taskId: number,
    title: string,
    projectName: string | undefined,
    projectId: number,
    merged: MergedConfig,
  ): Promise<void> {
    // Read-before-write: verify still ToDo
    const taskDetail = await this.mcp.getTask(taskId);
    if (taskDetail.status !== "ToDo") {
      console.log(`[agent] Task #${taskId} is no longer ToDo (${taskDetail.status}). Skipping.`);
      return;
    }

    // Pick up
    console.log(`[agent] Picking up task #${taskId}: ${title}`);
    await this.transitions.pickUp(taskId);

    // Resolve and sync repository
    const repo = resolveRepository(
      projectName,
      this.config.workingDirectory,
      merged.repositories,
      merged.repositoryUrl,
      merged.repositoryBranch,
      merged.repositoryAccessToken,
    );

    const workingDirectory = repo?.workingDirectory ?? this.config.workingDirectory;

    if (repo) {
      try {
        await ensureRepository(repo);
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        await this.transitions.error(
          taskId,
          title,
          `Failed to sync repository: ${message}`,
          projectId,
        );
        return;
      }
    }

    // Build effective config for the processor
    const effectiveConfig: EffectiveConfig = {
      model: merged.model,
      autoApproveTools: merged.autoApproveTools,
      dryRun: merged.dryRun,
      taskTimeoutSeconds: merged.taskTimeoutSeconds,
      systemPrompt: merged.systemPrompt,
      workingDirectory,
      codexApiKey: merged.codexApiKey,
      codexExecutablePath: merged.codexExecutablePath,
      mcpServers: merged.mcpServers,
    };

    // Process
    const result = await processTask(taskDetail, projectName, effectiveConfig);

    if (result.success) {
      console.log(`[agent] Task #${taskId} completed successfully.`);
      await this.transitions.complete(taskId, result.summary);
    } else {
      console.error(`[agent] Task #${taskId} failed: ${result.summary}`);
      await this.transitions.error(taskId, title, result.summary, projectId);
    }
  }

  private resolvePollingInterval(serverConfig?: AgentProfileConfiguration | null): number {
    return effective(
      this.config.pollingIntervalSeconds,
      serverConfig?.pollingIntervalSeconds ?? undefined,
      DEFAULTS.pollingIntervalSeconds,
    );
  }

  /** Merge server-side config with local config. Local wins for non-default values. */
  private mergeConfig(serverConfig?: AgentProfileConfiguration | null): MergedConfig {
    const s = serverConfig;

    return {
      pollingIntervalSeconds: effective(
        this.config.pollingIntervalSeconds, s?.pollingIntervalSeconds ?? undefined, DEFAULTS.pollingIntervalSeconds,
      ),
      model: this.config.model ?? s?.model ?? undefined,
      autoApproveTools: effective(
        this.config.autoApproveTools, s?.autoApproveTools ?? undefined, DEFAULTS.autoApproveTools,
      ),
      dryRun: effective(
        this.config.dryRun, s?.dryRun ?? undefined, DEFAULTS.dryRun,
      ),
      taskTimeoutSeconds: effective(
        this.config.taskTimeoutSeconds, s?.taskTimeoutSeconds ?? undefined, DEFAULTS.taskTimeoutSeconds,
      ),
      systemPrompt: this.config.systemPrompt ?? s?.systemPrompt ?? undefined,
      repositoryUrl: this.config.repositoryUrl ?? s?.repositoryUrl ?? undefined,
      repositoryBranch: this.config.repositoryBranch ?? s?.repositoryBranch ?? undefined,
      repositoryAccessToken: this.config.repositoryAccessToken ?? s?.repositoryAccessToken ?? undefined,
      repositories: mergeByKey(
        this.config.repositories ?? [],
        s?.repositories?.map((r) => ({
          projectName: r.projectName,
          url: r.url,
          branch: r.branch,
          accessToken: r.accessToken,
        })) ?? [],
        (r) => r.projectName.toLowerCase(),
      ),
      mcpServers: mergeByKey(
        this.config.mcpServers ?? [],
        s?.mcpServers?.map((m) => ({
          name: m.name,
          type: m.type as "http" | "stdio",
          url: m.url,
          authHeader: m.authHeader,
          command: m.command,
          args: m.args,
          env: m.env,
        })) ?? [],
        (m) => m.name.toLowerCase(),
      ),
      codexApiKey: this.config.codexApiKey ?? s?.codexApiKey ?? undefined,
      codexExecutablePath: this.config.codexExecutablePath ?? DEFAULTS.codexExecutablePath,
    };
  }

  private sleep(ms: number, signal?: AbortSignal): Promise<void> {
    return new Promise((resolve) => {
      const timer = setTimeout(resolve, ms);
      signal?.addEventListener("abort", () => {
        clearTimeout(timer);
        resolve();
      }, { once: true });
    });
  }
}

interface MergedConfig {
  pollingIntervalSeconds: number;
  model?: string;
  autoApproveTools: boolean;
  dryRun: boolean;
  taskTimeoutSeconds: number;
  systemPrompt?: string;
  repositoryUrl?: string;
  repositoryBranch?: string;
  repositoryAccessToken?: string;
  repositories: RepositoryConfig[];
  mcpServers: McpServerConfig[];
  codexApiKey?: string;
  codexExecutablePath?: string;
}

/** Merge two arrays by a key function. Local entries win on collision. */
function mergeByKey<T>(local: T[], server: T[], keyFn: (item: T) => string): T[] {
  const localKeys = new Set(local.map(keyFn));
  const serverOnly = server.filter((item) => !localKeys.has(keyFn(item)));
  return [...local, ...serverOnly];
}
