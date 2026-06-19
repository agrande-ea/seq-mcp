# Seq MCP Server — Design

Status: approved (2026-06-19)

## Purpose

A minimal, token-efficient MCP server that lets an LLM query a [Seq](https://datalust.co)
structured-log server. The centerpiece is running Seq SQL queries (including `group by`
aggregates); two convenience tools cover the common "what's broken" and "find these events"
cases. Returns terse plain text, never raw JSON, to keep token usage low.

Published as a .NET global tool to NuGet, following the same pattern as the StatusCake MCP
server.

## Architecture & stack

- **Language/SDK:** F#, `Microsoft.NET.Sdk.Web`, `net8.0`.
- **MCP SDK:** `ModelContextProtocol.AspNetCore` v1.4.0 (pulls in `ModelContextProtocol`).
  Tools are plain attributed .NET methods.
- **Transport:** Streamable HTTP — `AddMcpServer().WithHttpTransport().WithTools<SeqTools>()`
  + `app.MapMcp()`. Endpoint at root path (`/`); dev port `5250`.
- **HTTP client:** typed `HttpClient` registered with `AddHttpClient<SeqClient>()`; base
  address and `X-Seq-ApiKey` header configured in DI.
- **Backend:** calls Seq's HTTP API directly. No dependency on `seqcli` — the published tool
  must be self-contained (`dotnet tool install -g SeqMcp` and nothing else).

### Configuration

| Key | Env var | Default | Notes |
|-----|---------|---------|-------|
| `Seq:ServerUrl` | `SEQ__SERVERURL` | `http://localhost:5341` | Seq is self-hosted; no single public host. |
| `Seq:ApiKey` | `SEQ__APIKEY` | (none) | Optional. Sent as `X-Seq-ApiKey`. Missing/invalid → tool calls return graceful `isError` text rather than crashing. |

## Tools

All tools return compact human-readable text. Method names auto-convert to snake_case tool
names (`SeqQuery` → `seq_query`).

### `seq_query(sql, from?, to?)`

The centerpiece. Runs a Seq SQL query.

- `sql` — Seq SQL string. Supports list queries and `group by` aggregates.
- `from`, `to` — optional ISO-8601 timestamps bounding the query. When omitted, defaults to
  the **last 24 hours**.
- Renders both result shapes (aggregate Columns+Rows, and list/time-sliced) as a compact
  text table.
- **Row cap: 100.** When exceeded, append a trailing line: `… N more rows (refine your query)`.

### `recent_errors(minutes?)`

- `minutes` — window size, default `30`.
- Returns events at level Error/Fatal in the window as `time · level · message` lines.
- Capped (same spirit as the row cap).

### `search_events(filter, count?)`

- `filter` — a Seq filter expression.
- `count` — max events, default `30`, capped.
- Uses the events endpoint with rendered messages; returns compact `time · level · message`
  lines.

### Tool pattern

```fsharp
[<McpServerToolType>]
type SeqTools(client: SeqClient) =
    [<McpServerTool; Description("...terse description...")>]
    member _.SeqQuery(sql: string, ?from: string, ?to: string) : Task<string> =
        task { ... return "compact text" }
```

## Project layout

```
seq-mcp.sln
src/SeqMcp/
  Models.fs      # JSON record types for query + event responses ([<JsonPropertyName>])
  SeqClient.fs   # typed HttpClient wrapper: query + events calls
  Tools.fs       # [<McpServerToolType>] SeqTools with attributed methods
  Program.fs     # WebApplication host, DI, MapMcp
```

F# compile order matters — list files Models → Client → Tools → Program in the `.fsproj`
`<Compile Include=.../>` items.

## Packaging (.NET global tool)

In the `.fsproj` `<PropertyGroup>`:

```xml
<IsPackable>true</IsPackable>      <!-- REQUIRED: Web SDK disables packaging by default -->
<PackAsTool>true</PackAsTool>
<ToolCommandName>seq-mcp</ToolCommandName>
<PackageId>SeqMcp</PackageId>
<Version>0.1.0</Version>           <!-- CI overrides via -p:Version -->
<RollForward>Major</RollForward>
```

Verify with `dotnet pack` → `dotnet tool install -g SeqMcp --add-source ./nupkg`.

## CI/CD (GitHub Actions + NuGet Trusted Publishing / OIDC)

Two workflows:

- `.github/workflows/ci.yml` — build on push/PR to `main`.
- `.github/workflows/publish.yml` — on tag `v*.*.*`: derive version from tag, pack,
  `NuGet/login@v1`, `dotnet nuget push`. Needs `permissions: id-token: write` and
  `environment: nuget`.

Required setup outside the repo:

- **No NuGet API-key secret** (that's the point of trusted publishing).
- Repo variable `NUGET_USER` = nuget.org username (mandatory; `NuGet/login@v1` fails without it).
- A Trusted Publishing policy on nuget.org bound to repo owner, repo name, workflow file
  `publish.yml`, and environment `nuget`.
- A GitHub Environment named `nuget`.
- First publish of a brand-new package ID over OIDC may fail on ownership; if so, push `0.1.0`
  once with a classic API key to claim the ID, then OIDC CI works.

## Verification

1. `dotnet build`.
2. Run the server; `curl` an MCP handshake over Streamable HTTP: POST to `/` with
   `Accept: application/json, text/event-stream`; `initialize` returns an `Mcp-Session-Id`
   header to echo on later calls; send `notifications/initialized`, then `tools/list`
   (confirm all three tools advertised) and `tools/call` (confirm output).
3. A missing/unreachable Seq makes tool calls return `isError` gracefully rather than crash —
   so the handshake + tool advertisement can be verified without a live Seq.

## Open items to verify against a live Seq during implementation

The published Seq docs do not pin these down; confirm before finalizing `SeqClient`:

- Exact SQL query endpoint path and params — expected `GET /api/data/query?q=<sql>&from=<iso>&to=<iso>`.
- Query JSON response shape — `Columns` / `Rows` vs `Slices` / `Total`.
- Event search endpoint — expected `/api/events/signal` with `filter`, `count`, `render=true`,
  and the level filter for `recent_errors`.
- Confirmed: auth header is `X-Seq-ApiKey: <key>` (query-string `?apiKey=` also works).

## Out of scope (YAGNI)

- `list_signals` / saved-signal management.
- Live tail / streaming.
- Writing/ingesting events.
- `seqcli` subprocess backend (kept clean so it *could* be swapped later, but not built).
