import { readFileSync } from "fs";
import { resolve } from "path";

export interface McpServerConfig {
  name: string;
  type: "http" | "stdio";
  url?: string;
  authHeader?: string;
  command?: string;
  args?: string[];
  env?: Record<string, string>;
}

export interface RepositoryConfig {
  projectName: string;
  url: string;
  branch?: string;
  accessToken?: string;
}

export interface AgentConfig {
  teamware: {
    mcpUrl: string;
    personalAccessToken: string;
  };
  workingDirectory: string;
  pollingIntervalSeconds?: number;
  model?: string;
  autoApproveTools?: boolean;
  dryRun?: boolean;
  taskTimeoutSeconds?: number;
  systemPrompt?: string;
  anthropicApiKey?: string;
  repositoryUrl?: string;
  repositoryBranch?: string;
  repositoryAccessToken?: string;
  repositories?: RepositoryConfig[];
  mcpServers?: McpServerConfig[];
}

const DEFAULTS = {
  pollingIntervalSeconds: 60,
  autoApproveTools: true,
  dryRun: false,
  taskTimeoutSeconds: 600,
  repositoryBranch: "main",
} as const;

export function loadConfig(configPath?: string): AgentConfig {
  const path = configPath ?? resolve(process.cwd(), "config", "config.json");
  const raw = readFileSync(path, "utf-8");
  const config: AgentConfig = JSON.parse(raw);

  if (!config.teamware?.mcpUrl) {
    throw new Error("config: teamware.mcpUrl is required");
  }
  if (!config.teamware?.personalAccessToken) {
    throw new Error("config: teamware.personalAccessToken is required");
  }
  if (!config.workingDirectory) {
    throw new Error("config: workingDirectory is required");
  }

  return config;
}

/** Effective value after merge: local wins if set to non-default, otherwise server value. */
export function effective<T>(local: T | undefined, server: T | undefined, defaultVal: T): T {
  if (local !== undefined && local !== defaultVal) return local;
  return server ?? local ?? defaultVal;
}

export { DEFAULTS };
