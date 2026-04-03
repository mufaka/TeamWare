import { Codex } from "@openai/codex-sdk";
import type { ThreadOptions, CodexOptions } from "@openai/codex-sdk";
import type { AgentTaskDetail } from "../mcp/types.js";
import type { McpServerConfig } from "../config.js";
import { buildTaskPrompt } from "./prompt.js";

// Matches ConfigObject from the SDK (not exported)
type ConfigValue = string | number | boolean | ConfigValue[] | ConfigObject;
type ConfigObject = { [key: string]: ConfigValue };
import { DEFAULT_SYSTEM_PROMPT } from "./system-prompt.js";

export interface ProcessResult {
  success: boolean;
  summary: string;
}

export interface EffectiveConfig {
  model?: string;
  autoApproveTools: boolean;
  dryRun: boolean;
  taskTimeoutSeconds: number;
  systemPrompt?: string;
  workingDirectory: string;
  codexApiKey?: string;
  codexExecutablePath?: string;
  mcpServers: McpServerConfig[];
}

/**
 * Processes a single task using the Codex SDK.
 * Mirrors TaskProcessor from the .NET agent.
 */
export async function processTask(
  task: AgentTaskDetail,
  projectName: string | undefined,
  config: EffectiveConfig,
): Promise<ProcessResult> {
  const prompt = buildTaskPrompt(task, projectName);
  const systemPrompt = config.systemPrompt ?? DEFAULT_SYSTEM_PROMPT;

  // Build Codex SDK options
  const codexOptions: CodexOptions = {};

  if (config.codexExecutablePath) {
    codexOptions.codexPathOverride = config.codexExecutablePath;
  }
  if (config.codexApiKey) {
    codexOptions.apiKey = config.codexApiKey;
  }

  // Pass system prompt and MCP servers via config overrides
  const codexConfig: ConfigObject = {
    developer_instructions: systemPrompt,
  };

  // Add MCP servers to config
  const mcpServers: ConfigObject = {};
  for (const server of config.mcpServers) {
    if (server.type === "http" && server.url) {
      const serverConfig: ConfigObject = { url: server.url };
      if (server.authHeader) {
        // Pass via env var to avoid leaking in config
        serverConfig.bearer_token_env_var = `MCP_${server.name.toUpperCase().replace(/[^A-Z0-9]/g, "_")}_TOKEN`;
      }
      mcpServers[server.name] = serverConfig;
    } else if (server.type === "stdio" && server.command) {
      const serverConfig: ConfigObject = {
        command: server.command,
      };
      if (server.args?.length) {
        serverConfig.args = server.args;
      }
      if (server.env) {
        serverConfig.env = server.env;
      }
      mcpServers[server.name] = serverConfig;
    }
  }

  if (Object.keys(mcpServers).length > 0) {
    codexConfig.mcp_servers = mcpServers;
  }

  codexOptions.config = codexConfig;

  // Build environment with MCP auth tokens
  // authHeader from the server is a full Authorization value (e.g. "Bearer xyz").
  // bearer_token_env_var expects just the token — Codex prepends "Bearer " itself.
  const env: Record<string, string> = { ...process.env as Record<string, string> };
  for (const server of config.mcpServers) {
    if (server.type === "http" && server.authHeader) {
      const envKey = `MCP_${server.name.toUpperCase().replace(/[^A-Z0-9]/g, "_")}_TOKEN`;
      env[envKey] = server.authHeader.replace(/^Bearer\s+/i, "");
    }
  }
  codexOptions.env = env;

  const codex = new Codex(codexOptions);

  // Build thread options
  const threadOptions: ThreadOptions = {
    workingDirectory: config.workingDirectory,
  };

  if (config.model) {
    threadOptions.model = config.model;
  }

  if (config.dryRun) {
    threadOptions.sandboxMode = "read-only";
    threadOptions.approvalPolicy = "untrusted";
  } else if (config.autoApproveTools) {
    threadOptions.sandboxMode = "danger-full-access";
    threadOptions.approvalPolicy = "on-request";
  } else {
    threadOptions.sandboxMode = "danger-full-access";
    threadOptions.approvalPolicy = "on-failure";
  }

  const thread = codex.startThread(threadOptions);

  // Run with timeout
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), config.taskTimeoutSeconds * 1000);

  try {
    const result = await thread.run(prompt, { signal: controller.signal });

    const summary = result.finalResponse || "Task completed.";
    return { success: true, summary };
  } catch (err: unknown) {
    const message = err instanceof Error ? err.message : String(err);

    if (controller.signal.aborted) {
      return {
        success: false,
        summary: `Task timed out after ${config.taskTimeoutSeconds} seconds. ${message}`,
      };
    }

    return { success: false, summary: `Error during task processing: ${message}` };
  } finally {
    clearTimeout(timeout);
  }
}
