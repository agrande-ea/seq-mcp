# seq-mcp

A minimal, token-efficient [MCP](https://modelcontextprotocol.io) server for querying a
[Seq](https://datalust.co) structured-log server. It exposes Seq SQL querying (including
`group by` aggregates) plus two convenience tools, and returns terse plain text rather than
raw JSON to keep token usage low.

## Tools

| Tool | Description |
|------|-------------|
| `seq_query(sql, from?, to?)` | Run a Seq SQL query. Supports aggregates/grouping, e.g. `select count(*) as Count from stream group by @Level`. `from`/`to` are optional ISO-8601 UTC bounds (default: last 24h). Results capped at 100 rows. |
| `recent_errors(minutes?)` | Recent Error/Fatal events as `Time · Level · Message` lines, newest first (default: last 30 min). |
| `search_events(filter, count?)` | Events matching a Seq filter expression (e.g. `@Exception like '%timeout%'`), newest first (default: 30, last 24h). |

## Install

```sh
dotnet tool install -g SeqMcp
```

This installs the `seq-mcp` command. It runs an HTTP MCP server on `http://localhost:5250`
(override with `ASPNETCORE_URLS`).

## Configuration

| Setting | Environment variable | Default |
|---------|----------------------|---------|
| Seq server URL | `SEQ__SERVERURL` | `http://localhost:5341` |
| Seq API key | `SEQ__APIKEY` | _(none)_ |

The API key is sent as the `X-Seq-ApiKey` header. It is optional if your Seq instance allows
anonymous read; when the key is missing or Seq is unreachable, tool calls return a graceful
error message rather than failing the server.

### Example MCP client config

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

Start the server with your environment set:

```sh
SEQ__SERVERURL=https://seq.example.com SEQ__APIKEY=your-api-key seq-mcp
```

## Build from source

```sh
dotnet build seq-mcp.sln -c Release
dotnet run --project src/SeqMcp
```

## License

MIT
