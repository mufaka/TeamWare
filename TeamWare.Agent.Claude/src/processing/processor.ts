import { query } from "@anthropic-ai/claude-agent-sdk";
import type { SDKMessage, SDKResultMessage, McpHttpServerConfig, McpStdioServerConfig, McpServerConfig as SdkMcpServerConfig, PermissionMode, Options } from "@anthropic-ai/claude-agent-sdk";
import type { AgentTaskDetail } from "../mcp/types.js";
import type { McpServerConfig } from "../config.js";
import { buildTaskPrompt } from "./prompt.js";
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
  anthropicApiKey?: string;
  mcpServers: McpServerConfig[];
  teamwareMcpUrl: string;
  teamwarePat: string;
}

/**
 * Processes a single task using the Claude Agent SDK.
 * Mirrors TaskProcessor from the .NET agent.
 *
 * The Claude Agent SDK wraps Claude Code CLI, providing full autonomous
 * coding capabilities: file read/write/edit, bash execution, web search,
 * and built-in MCP server connectivity.
 */
export async function processTask(
  task: AgentTaskDetail,
  projectName: string | undefined,
  config: EffectiveConfig,
): Promise<ProcessResult> {
  const prompt = buildTaskPrompt(task, projectName);
  const systemPrompt = config.systemPrompt ?? DEFAULT_SYSTEM_PROMPT;

  // Build MCP server map for Claude Agent SDK
  const mcpServers: Record<string, SdkMcpServerConfig> = {
    // Always include TeamWare's MCP server so Claude can call tools directly
    teamware: {
      type: "http",
      url: config.teamwareMcpUrl,
      headers: {
        Authorization: `Bearer ${config.teamwarePat}`,
      },
    } satisfies McpHttpServerConfig,
  };

  // Add any additional configured MCP servers
  for (const server of config.mcpServers) {
    if (server.type === "http" && server.url) {
      const serverConfig: McpHttpServerConfig = {
        type: "http",
        url: server.url,
      };
      if (server.authHeader) {
        serverConfig.headers = {
          Authorization: server.authHeader,
        };
      }
      mcpServers[server.name] = serverConfig;
    } else if (server.type === "stdio" && server.command) {
      const serverConfig: McpStdioServerConfig = {
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

  // Determine permission mode based on config
  let permissionMode: PermissionMode;
  if (config.dryRun) {
    permissionMode = "plan";
  } else if (config.autoApproveTools) {
    permissionMode = "bypassPermissions";
  } else {
    permissionMode = "default";
  }

  // Build environment — inject API key if configured
  const env: Record<string, string | undefined> = { ...process.env };
  if (config.anthropicApiKey) {
    env.ANTHROPIC_API_KEY = config.anthropicApiKey;
  }

  // Build query options
  const abortController = new AbortController();
  const timeout = setTimeout(() => abortController.abort(), config.taskTimeoutSeconds * 1000);

  const options: Options = {
    model: config.model,
    systemPrompt,
    maxTurns: 200,
    permissionMode,
    cwd: config.workingDirectory,
    mcpServers,
    env,
    abortController,
    persistSession: false,
  };

  // bypassPermissions requires explicit opt-in
  if (permissionMode === "bypassPermissions") {
    options.allowDangerouslySkipPermissions = true;
  }

  try {
    let resultSummary = "";

    // query() takes { prompt, options } and returns an AsyncGenerator<SDKMessage>
    for await (const message of query({ prompt, options })) {
      // Look for the final result message
      if (message.type === "result") {
        const resultMsg = message as SDKResultMessage;
        if (resultMsg.subtype === "success" && "result" in resultMsg) {
          resultSummary = resultMsg.result;
        } else if (resultMsg.is_error && "errors" in resultMsg) {
          return {
            success: false,
            summary: `Task processing error: ${resultMsg.errors.join("; ")}`,
          };
        }
      }
    }

    return { success: true, summary: resultSummary || "Task completed." };
  } catch (err: unknown) {
    const errMessage = err instanceof Error ? err.message : String(err);

    if (abortController.signal.aborted) {
      return {
        success: false,
        summary: `Task timed out after ${config.taskTimeoutSeconds} seconds. ${errMessage}`,
      };
    }

    return { success: false, summary: `Error during task processing: ${errMessage}` };
  } finally {
    clearTimeout(timeout);
  }
}
