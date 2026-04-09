# Fix Idea: Issue #282 — Agents fail when TeamWare MCP is temporarily unavailable

**Issue:** [#282](https://github.com/mufaka/TeamWare/issues/282)
**Severity:** Critical — affects all production agents
**Trigger:** Deploying TeamWare.Web causes running agents to permanently lose their MCP session

---

## Root Cause Analysis

All three agent implementations (Copilot/.NET, Claude/TypeScript, Codex/TypeScript) share the same architectural flaw: **the MCP client is created once at startup and never recreated**.

### TeamWare.Agent (C# / .NET)

In `AgentHostedService.RunPollingLoopAsync`, the MCP client is created *once* before the polling loop begins:

```csharp
// AgentHostedService.cs:79-103
mcpClient = await _mcpClientFactory.CreateAsync(agent, cancellationToken);
// ...
var pollingLoop = new AgentPollingLoop(agent, mcpClient, ...);
await pollingLoop.RunAsync(cancellationToken);
```

`TeamWareMcpClient` wraps a `McpClient` which holds a `StreamableHttpClientSessionTransport`. When TeamWare.Web restarts, the server-side session state (and potentially the SSE stream) is destroyed. The `McpClient` instance has no reconnection logic — subsequent `CallToolAsync` calls throw `TaskCanceledException` forever.

Additionally, if `CreateAsync` fails at startup (`AgentHostedService.cs:86-93`), the entire polling loop is abandoned with no retry — a startup-time deployment race also kills the agent permanently.

### TeamWare.Agent.Claude & TeamWare.Agent.Codex (TypeScript)

Both TypeScript agents have the identical pattern:

```typescript
// polling/loop.ts:33-35
async start(signal?: AbortSignal): Promise<void> {
    this.running = true;
    await this.mcp.connect();   // One-time connection
    // ... polling loop runs with this single connection forever
```

The `TeamWareMcpClient.connect()` creates the `StreamableHTTPClientTransport` and calls `client.connect(transport)` once. If the server goes away, the stale `Client` instance throws on every subsequent `callTool`, and the error is caught generically in the polling loop without any reconnection attempt.

---

## Proposed Fix

The fix needs to be applied to all three agent projects. The core idea is the same for each: **detect consecutive MCP failures and recreate the client connection**.

### Strategy: Consecutive Failure Counter with Auto-Reconnect

Rather than adding keep-alive/heartbeat complexity, leverage the existing polling loop structure. The polling loop already catches exceptions per cycle. Add a **consecutive failure counter** that, after N failures, triggers a dispose-and-reconnect of the MCP client.

### Design Parameters

| Parameter | Suggested Default | Notes |
|---|---|---|
| Max consecutive failures before reconnect | 3 | Configurable via `AgentIdentityOptions` / config |
| Reconnect backoff | Reuse `PollingIntervalSeconds` | No additional delay needed — the polling sleep already provides spacing |
| Max reconnect attempts before giving up | ∞ (never give up) | Agents should be resilient to indefinite outages |

### C# Agent Changes

#### 1. `AgentPollingLoop.cs` — Add failure tracking and reconnect logic

Move the MCP client from an immutable constructor dependency to a resettable field. Add a consecutive failure counter. When the counter exceeds the threshold:

1. Log a warning.
2. Dispose the old `ITeamWareMcpClient`.
3. Create a new one via `ITeamWareMcpClientFactory`.
4. Reset the counter.
5. Rebuild `StatusTransitionHandler` with the new client.

```
Fields to add:
- private int _consecutiveFailures = 0;
- private const int MaxConsecutiveFailuresBeforeReconnect = 3;
- private ITeamWareMcpClient _mcpClient;  (change from readonly)
- private readonly ITeamWareMcpClientFactory _mcpClientFactory;  (new dependency)

RunAsync changes:
- On successful ExecuteCycleAsync: reset _consecutiveFailures = 0
- On exception: increment _consecutiveFailures
  - If >= threshold: call ReconnectAsync()

New method: ReconnectAsync()
- DisposeAsync old client (swallow errors)
- Call _mcpClientFactory.CreateAsync(...)
- Rebuild _statusHandler
- Reset counter
- Log reconnection
```

The constructor must accept `ITeamWareMcpClientFactory` in addition to (or instead of) the pre-built `ITeamWareMcpClient`. The factory is already available in `AgentHostedService`.

#### 2. `AgentHostedService.cs` — Add startup retry and pass factory to loop

Currently, if `CreateAsync` fails at startup, the loop never starts. Add retry logic:

```
RunPollingLoopAsync changes:
- Wrap the initial CreateAsync in a retry loop (e.g., 5 attempts with exponential backoff)
- Pass _mcpClientFactory to AgentPollingLoop so it can reconnect autonomously
```

#### 3. `ITeamWareMcpClient` — No changes needed

The interface already extends `IAsyncDisposable`, which is sufficient for the dispose-and-recreate pattern.

### TypeScript Agent Changes (Claude & Codex)

Both agents have nearly identical code, so the same fix applies to both.

#### 1. `mcp/client.ts` — Add reconnect method

```
New method: reconnect()
- Call disconnect() (swallow errors)
- Call connect()
- Log reconnection

Harden callTool:
- Catch connection-level errors (not tool-level business errors)
- Expose a method or let the loop handle reconnect decisions
```

#### 2. `polling/loop.ts` — Add failure tracking

```
Fields to add:
- private consecutiveFailures = 0;
- private readonly maxFailuresBeforeReconnect = 3;

executeCycle changes (in the catch block of the while loop):
- Increment consecutiveFailures
- If >= threshold:
  - Log warning
  - Call this.mcp.reconnect()
  - Reset counter

On successful cycle:
- Reset consecutiveFailures = 0
```

#### 3. `start()` — Add initial connection retry

Currently if `this.mcp.connect()` fails, the loop never starts. Add retry:

```typescript
// Retry initial connection with backoff
let connected = false;
while (!connected && this.running && !signal?.aborted) {
    try {
        await this.mcp.connect();
        connected = true;
    } catch (err) {
        console.error("[agent] Failed to connect to TeamWare MCP, retrying...", err);
        await this.sleep(pollingInterval * 1000, signal);
    }
}
```

---

## Error Classification

Not all errors should trigger reconnection. The counter should only increment for **transport-level** errors, not business-logic errors from MCP tools:

| Error Type | Trigger Reconnect? | Examples |
|---|---|---|
| `TaskCanceledException` | ✅ Yes | HTTP timeout, connection refused |
| `HttpRequestException` | ✅ Yes | DNS failure, connection reset, 502/503 |
| `McpToolException` | ❌ No | Tool returned an error response (server is alive) |
| `JsonException` (deserialization) | ❌ No | Bad response format (server is alive) |
| `OperationCanceledException` (shutdown) | ❌ No | Normal cancellation — propagate |

In the C# agent, the `ExecuteCycleAsync` catch block should classify the exception:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
{
    _logger.LogError(ex, "Error in polling cycle for agent '{AgentName}'", _options.Name);

    if (IsTransportError(ex))
        _consecutiveFailures++;
    else
        _consecutiveFailures = 0; // Business error = server is reachable
}
```

Similarly in TypeScript, check for network-level errors vs. MCP tool errors.

---

## Files to Modify

### TeamWare.Agent (C#)

| File | Change |
|---|---|
| `Pipeline/AgentPollingLoop.cs` | Add failure counter, reconnect logic, accept factory |
| `AgentHostedService.cs` | Add startup retry, pass factory to polling loop |
| `Mcp/TeamWareMcpClient.cs` | (Optional) Add `IsConnected` property for diagnostics |

### TeamWare.Agent.Claude (TypeScript)

| File | Change |
|---|---|
| `src/mcp/client.ts` | Add `reconnect()` method |
| `src/polling/loop.ts` | Add failure counter, reconnect trigger, startup retry |

### TeamWare.Agent.Codex (TypeScript)

| File | Change |
|---|---|
| `src/mcp/client.ts` | Add `reconnect()` method |
| `src/polling/loop.ts` | Add failure counter, reconnect trigger, startup retry |

### Tests

| File | Change |
|---|---|
| `TeamWare.Agent.Tests/Pipeline/AgentPollingLoopTests.cs` | Add tests for reconnect after N failures, startup retry |

---

## Testing Plan

1. **Unit Test:** Mock `ITeamWareMcpClient` to throw `TaskCanceledException` for 3 consecutive cycles, verify reconnect is triggered via factory, verify counter resets on success.
2. **Unit Test:** Mock `McpToolException` — verify it does NOT trigger reconnect.
3. **Unit Test:** Startup failure retry — mock factory to fail twice then succeed.
4. **Integration Test:** Deploy TeamWare.Web while agent is running, verify agent recovers within `MaxConsecutiveFailures × PollingInterval` seconds.

---

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Reconnect during active task processing | Reconnect only happens between polling cycles, never mid-task |
| Rapid reconnect storms | Bounded by polling interval (natural backoff) |
| Memory leak from undisposed old clients | Explicit `DisposeAsync` before recreate; log if dispose fails |
| Server-side session ID mismatch after reconnect | `McpClient.CreateAsync` creates a fresh session; StreamableHTTP is stateless enough for tool calls |
