# seq-mcp

A minimal, token-efficient [MCP](https://modelcontextprotocol.io) server for querying a
[Seq](https://datalust.co) structured-log server. It exposes Seq SQL querying (including
`group by` aggregates) plus two convenience tools, and returns terse plain text rather than
raw JSON to keep token usage low.

## Tools

| Tool | Description |
|------|-------------|
| `seq_query(sql, from?, to?, signal?)` | Run a Seq SQL query. Supports aggregates/grouping, e.g. `select count(*) as Count from stream group by @Level`. `from`/`to` are optional ISO-8601 UTC bounds (default: last 24h). Optionally scope to a saved `signal` id. Results capped at 100 rows. |
| `recent_errors(minutes?)` | Most recent Error/Fatal events (up to 20) as `Id · Time · Level · Message` lines, newest first (default: last 30 min). |
| `search_events(filter, count?, signal?)` | Events matching a Seq filter expression (e.g. `@Exception like '%timeout%'`), newest first (default: 30, last 24h). Optionally scope to a saved `signal` id. |
| `get_event(id)` | Full detail for one event (the `Id` from the list tools): rendered message, exception/stack trace, and all properties. |
| `list_signals(nameFilter?)` | Saved signals as `Id · Title` lines; the `Id` can scope `seq_query`/`search_events`. Pass a name substring to narrow the list. |

## Install

```sh
dotnet tool install -g SeqMcp
```

This installs the `seq-mcp` command, which supports two transports:

- **stdio** (`seq-mcp --stdio`) — the MCP client launches and manages the process, speaking
  JSON-RPC over stdin/stdout. Recommended for local single-user clients like Claude Code.
- **HTTP** (`seq-mcp`) — a long-running Streamable HTTP server on `http://localhost:5250`
  (override with `ASPNETCORE_URLS`) that clients connect to by URL.

## Configuration

| Setting | Environment variable | Default |
|---------|----------------------|---------|
| Seq server URL | `SEQ__SERVERURL` | `http://localhost:5341` |
| Seq API key | `SEQ__APIKEY` | _(none)_ |

The API key is sent as the `X-Seq-ApiKey` header. It is optional if your Seq instance allows
anonymous read; when the key is missing or Seq is unreachable, tool calls return a graceful
error message rather than failing the server.

### Use with Claude Code

stdio (recommended) — Claude launches and manages the process:

```sh
claude mcp add seq -e SEQ__SERVERURL=https://seq.example.com -e SEQ__APIKEY=your-api-key -- seq-mcp --stdio
```

HTTP — run the server yourself, then point Claude at the URL:

```sh
SEQ__SERVERURL=https://seq.example.com SEQ__APIKEY=your-api-key seq-mcp   # in one terminal
claude mcp add --transport http seq http://localhost:5250
```

### Example MCP client config (HTTP)

```json
{
  "mcpServers": {
    "seq": {
      "type": "http",
      "url": "http://localhost:5250"
    }
  }
}
```

## Build from source

```sh
dotnet build seq-mcp.sln -c Release
dotnet run --project src/SeqMcp
```

## License

MIT
