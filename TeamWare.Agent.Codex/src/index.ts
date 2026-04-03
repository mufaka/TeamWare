import { loadConfig } from "./config.js";
import { PollingLoop } from "./polling/loop.js";

const configPath = process.argv[2] ?? undefined;

let config;
try {
  config = loadConfig(configPath);
} catch (err) {
  console.error("[agent] Failed to load config:", err instanceof Error ? err.message : err);
  process.exit(1);
}

console.log(`[agent] TeamWare Codex Agent starting.`);
console.log(`[agent] MCP endpoint: ${config.teamware.mcpUrl}`);
console.log(`[agent] Working directory: ${config.workingDirectory}`);

const loop = new PollingLoop(config);

// Graceful shutdown
const controller = new AbortController();

function shutdown() {
  console.log("[agent] Shutting down...");
  loop.stop();
  controller.abort();
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

loop.start(controller.signal).catch((err) => {
  console.error("[agent] Fatal error:", err);
  process.exit(1);
});
