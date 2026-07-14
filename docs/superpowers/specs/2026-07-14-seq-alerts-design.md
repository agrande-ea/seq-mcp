# Seq Alerts (read-only) — Design

Status: approved (2026-07-14)

## Purpose

Expose Seq's alerts API through `seq-mcp` so an LLM (or CLI user) can answer two questions:
"what alerts are configured?" and "which alerts are firing right now?". Read-only, matching the
existing observability posture of the server (query / errors / search / event / signals). No
alert creation, update, or deletion.

## Scope

Three new read-only capabilities, added as a slice through every existing layer. No new files
and no structural change — the layered flow in `CLAUDE.md` is preserved:

`Models.fs → SeqClient.fs → Render.fs → Commands.fs → Tools.fs / Cli.fs → README.md`

| Command | Seq endpoint(s) | Output |
|---|---|---|
| `listAlerts nameFilter` | `GET /api/alerts` | `Id · Title · [disabled]` lines, filtered by title substring (mirrors `listSignals`) |
| `getAlert id` | `GET /api/alerts/{id}` | Detail: title, enabled/disabled, signals, notification channels, owner, shared, condition |
| `alertState` | `GET /api/alertstate` + `GET /api/alerts` | `Id · Title · Status · Occurrences · Since` lines; joins alert titles by Id |

Out of scope (YAGNI): POST/PUT/DELETE on alerts, `/api/alerts/template`, `/api/alerts/resources`,
`/api/alertstate/{id}`, and deleting alert state. These require `Project` write permission and
are not part of "consuming" alerts.

## Data model (`Models.fs`)

New DTOs with explicit `[<JsonPropertyName>]` attributes, deserialized with the existing
case-insensitive options. Field presence is guarded defensively (`isNull (box …)`), matching the
existing rendering code.

- **`Alert`** — `Id`, `Title`, `IsDisabled`, `Signals` (array), `NotificationChannels` (array),
  `OwnerId`, `IsShared`, plus condition-related field(s) once confirmed against a live payload.
- **`AlertState`** — alert `Id`, `Status` (e.g. Firing/Resolved), occurrence count, first-occurrence
  timestamp.

Exact wire-level JSON names/shape are confirmed against a live Seq server during implementation
(see Verification) and the `[<JsonPropertyName>]` attributes adjusted if they differ from the
documented conceptual fields. This mirrors how the original tools were built ("Fix Seq API
integration after live testing").

## Client (`SeqClient.fs`)

Three new members following the existing `send` + `JsonSerializer.Deserialize` pattern. All HTTP
and transport failures already flow through `send`, which yields graceful, actionable 401/403 and
unreachable-server messages — no new error handling needed.

- `ListAlertsAsync() : Task<Alert[]>` → `api/alerts`
- `GetAlertAsync(id) : Task<Alert>` → `api/alerts/{id}` (id URL-escaped, as `GetEventAsync`)
- `AlertStateAsync() : Task<AlertState[]>` → `api/alertstate`

## Commands + rendering

- `Commands.listAlerts client nameFilter` → `Render.alerts` (case-insensitive title-substring
  filter, capped list — mirrors `listSignals`).
- `Commands.getAlert client id` → `Render.alertDetail`.
- `Commands.alertState client` → calls `AlertStateAsync` and `ListAlertsAsync`, builds an
  `Id → Title` map, → `Render.alertState`. A state entry with no matching alert falls back to
  showing the raw Id.

New `Render` functions return terse plain text, never raw JSON (the module's contract). Empty
results return `"No alerts."` / `"No alerts firing."` respectively.

## Front-ends

- **`Tools.fs`** — three `[<McpServerTool>]` members (`ListAlerts`, `GetAlert`, `AlertState`)
  wrapping the commands through the existing `run` catch-all, with Descriptions in the same style
  as the current tools. Snake-case tool names: `list_alerts`, `get_alert`, `alert_state`.
- **`Cli.fs`** — three new `CliSpec` entries and dispatch arms:
  - `alerts` — arg: none; opt: `--filter` (title substring)
  - `alert` — arg: `id`
  - `alert-state` — arg: none
  Keeps `Cli.specs` in sync (the source of truth for `--help` and `seq-mcp commands`) per CLAUDE.md.
- **`README.md`** — three new rows in the Tools table and matching CLI examples.

## Error handling & permissions

- Reuses the existing `send` pipeline; no new error paths.
- `GET /api/alertstate` may require the `Project` permission. A read-only API key can therefore
  be rejected on `alert_state` specifically; the existing 401/403 handling surfaces this as a
  clear message rather than a crash. Documented as a caveat; not worked around.

## Verification

The repo has no test project; verification matches how the existing tools were validated:

1. `dotnet build seq-mcp.sln -c Release`.
2. Exercise each verb against a live Seq server via the CLI:
   `seq-mcp alerts`, `seq-mcp alert <id>`, `seq-mcp alert-state`.
3. Confirm live JSON field names/shape match the models; adjust `[<JsonPropertyName>]` if needed.
4. Confirm `alert_state`'s title join renders correctly and that a missing-title entry falls back
   to the raw Id.
5. Confirm `alertstate` `Project`-permission behavior degrades gracefully on a read-only key.
