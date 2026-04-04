import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StreamableHTTPClientTransport } from "@modelcontextprotocol/sdk/client/streamableHttp.js";
import type {
  AgentProfile,
  AgentTask,
  AgentTaskDetail,
} from "./types.js";

export interface TeamWareMcpClientOptions {
  mcpUrl: string;
  personalAccessToken: string;
}

/**
 * MCP client wrapper for TeamWare. Mirrors ITeamWareMcpClient from the .NET agent.
 * Uses the 6 MCP tools that make up the agent protocol contract.
 */
export class TeamWareMcpClient {
  private client: Client | null = null;
  private readonly options: TeamWareMcpClientOptions;

  constructor(options: TeamWareMcpClientOptions) {
    this.options = options;
  }

  async connect(): Promise<void> {
    const transport = new StreamableHTTPClientTransport(
      new URL(this.options.mcpUrl),
      {
        requestInit: {
          headers: {
            Authorization: `Bearer ${this.options.personalAccessToken}`,
          },
        },
      },
    );

    this.client = new Client({ name: "teamware-claude-agent", version: "1.0.0" });
    await this.client.connect(transport);
  }

  async disconnect(): Promise<void> {
    if (this.client) {
      await this.client.close();
      this.client = null;
    }
  }

  private ensureConnected(): Client {
    if (!this.client) {
      throw new Error("MCP client not connected. Call connect() first.");
    }
    return this.client;
  }

  private async callTool(name: string, args: Record<string, unknown> = {}): Promise<unknown> {
    const client = this.ensureConnected();
    const result = await client.callTool({ name, arguments: args });

    if (result.isError) {
      const content = result.content as Array<{ type: string; text?: string }> | undefined;
      const errorText = content
        ?.filter((c) => c.type === "text" && c.text)
        .map((c) => c.text)
        .join("\n") ?? "Unknown MCP error";
      throw new Error(`MCP tool '${name}' failed: ${errorText}`);
    }

    const content = result.content as Array<{ type: string; text?: string }> | undefined;
    const textContent = content?.find((c) => c.type === "text" && c.text);
    if (!textContent?.text) {
      throw new Error(`MCP tool '${name}' returned no text content`);
    }

    return JSON.parse(textContent.text);
  }

  async getMyProfile(): Promise<AgentProfile> {
    return (await this.callTool("get_my_profile")) as AgentProfile;
  }

  async getMyAssignments(): Promise<AgentTask[]> {
    return (await this.callTool("my_assignments")) as AgentTask[];
  }

  async getTask(taskId: number): Promise<AgentTaskDetail> {
    return (await this.callTool("get_task", { taskId })) as AgentTaskDetail;
  }

  async updateTaskStatus(taskId: number, status: string): Promise<void> {
    await this.callTool("update_task_status", { taskId, status });
  }

  async addComment(taskId: number, content: string): Promise<void> {
    await this.callTool("add_comment", { taskId, content });
  }

  async postLoungeMessage(content: string, projectId?: number): Promise<void> {
    const args: Record<string, unknown> = { content };
    if (projectId !== undefined) {
      args.projectId = projectId;
    }
    await this.callTool("post_lounge_message", args);
  }
}
