# Seq Alerts (read-only) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three read-only Seq alerts capabilities — `list_alerts`, `get_alert`, `alert_state` — across the existing layered front-ends (MCP + CLI).

**Architecture:** A vertical slice added bottom-up through the existing layers in F# compile order (`Models → SeqClient → Render → Commands → Tools → Cli`). Each task builds cleanly on its own. The shared `Commands` core is called by both the MCP tools (`Tools.fs`) and the CLI (`Cli.fs`), so behaviour stays identical. `alert_state` joins runtime state with alert titles from the config endpoint.

**Tech Stack:** F#, .NET 8, `System.Text.Json`, `ModelContextProtocol.AspNetCore` 1.4.0. Seq HTTP API (`GET /api/alerts`, `GET /api/alerts/{id}`, `GET /api/alertstate`).

## Global Constraints

- CRLF line endings — do not introduce LF-only files (this is a .NET repo on Windows).
- F# compile order is fixed in `SeqMcp.fsproj`; all target files are already listed. Do NOT add new files.
- All new capabilities go in `Commands.fs`; `Tools.fs` and `Cli.fs` stay thin front-ends. Keep `Cli.specs` in sync with every new verb (per `CLAUDE.md`).
- Rendering returns terse plain text, never raw JSON.
- Reuse the existing `SeqClient.send` pipeline for all HTTP calls (it already produces graceful 401/403 and unreachable-server messages).
- Build command: `dotnet build seq-mcp.sln -c Release`.
- No test project exists; the per-task gate is a successful build. Live behaviour is verified in Task 6 against a real Seq server.

---

### Task 1: Alert data models

**Files:**
- Modify: `src/SeqMcp/Models.fs` (append after the `Signal` type, end of file)

**Interfaces:**
- Consumes: nothing (leaf types).
- Produces:
  - `type Alert = { Id: string; Title: string; IsDisabled: bool; OwnerId: string; IsShared: bool; Signals: JsonElement[]; NotificationChannels: JsonElement[] }`
  - `type AlertState = { Id: string; AlertId: string; Status: string; Occurrences: int; FirstOccurrence: string }`

`Signals` / `NotificationChannels` are `JsonElement[]` (not typed) so deserialization never fails on an unknown array shape; they render defensively via the existing `Render.cell`. Field names are provisional and confirmed against a live payload in Task 6.

- [ ] **Step 1: Append the two record types to `Models.fs`**

```fsharp
/// A configured Seq alert (a named rule that triggers on matching events) from
/// GET /api/alerts. Signals/NotificationChannels are kept as raw JSON elements
/// because their element shape is server-defined; they render via Render.cell.
type Alert =
    { [<JsonPropertyName("Id")>]
      Id: string

      [<JsonPropertyName("Title")>]
      Title: string

      [<JsonPropertyName("IsDisabled")>]
      IsDisabled: bool

      [<JsonPropertyName("OwnerId")>]
      OwnerId: string

      [<JsonPropertyName("IsShared")>]
      IsShared: bool

      [<JsonPropertyName("Signals")>]
      Signals: JsonElement[]

      [<JsonPropertyName("NotificationChannels")>]
      NotificationChannels: JsonElement[] }

/// Current runtime state of an alert from GET /api/alertstate: whether it is
/// firing, how many times, and since when. AlertId is the join key back to Alert;
/// some Seq versions carry it on the state entity's own Id instead.
type AlertState =
    { [<JsonPropertyName("Id")>]
      Id: string

      [<JsonPropertyName("AlertId")>]
      AlertId: string

      [<JsonPropertyName("Status")>]
      Status: string

      [<JsonPropertyName("Occurrences")>]
      Occurrences: int

      [<JsonPropertyName("FirstOccurrence")>]
      FirstOccurrence: string }
```

- [ ] **Step 2: Build**

Run: `dotnet build seq-mcp.sln -c Release`
Expected: `Build succeeded`. `JsonElement`/`JsonPropertyName` are already opened at the top of `Models.fs`.

- [ ] **Step 3: Commit**

```bash
git add src/SeqMcp/Models.fs
git commit -m "Add Alert and AlertState models"
```

---

### Task 2: SeqClient alert methods

**Files:**
- Modify: `src/SeqMcp/SeqClient.fs` (add three members after `ListSignalsAsync`, before the closing of the type)

**Interfaces:**
- Consumes: `Alert`, `AlertState` (Task 1); private `send`, `jsonOptions` (existing).
- Produces:
  - `member ListAlertsAsync : unit -> Task<Alert[]>`
  - `member GetAlertAsync : string -> Task<Alert>`
  - `member AlertStateAsync : unit -> Task<AlertState[]>`

- [ ] **Step 1: Add the three members to `SeqClient.fs`**

Insert directly after the `ListSignalsAsync` member (keep them inside the `SeqClient` type):

```fsharp
    /// List configured alerts (shared and owned).
    member _.ListAlertsAsync() : Task<Alert[]> =
        task {
            let! body = send "api/alerts?shared=true"
            return JsonSerializer.Deserialize<Alert[]>(body, jsonOptions)
        }

    /// Fetch a single alert by id.
    member _.GetAlertAsync(id: string) : Task<Alert> =
        task {
            let url = "api/alerts/" + Uri.EscapeDataString id
            let! body = send url
            return JsonSerializer.Deserialize<Alert>(body, jsonOptions)
        }

    /// List the current runtime state of alerts (which are firing, and since when).
    /// May require an API key with the Project permission.
    member _.AlertStateAsync() : Task<AlertState[]> =
        task {
            let! body = send "api/alertstate"
            return JsonSerializer.Deserialize<AlertState[]>(body, jsonOptions)
        }
```

- [ ] **Step 2: Build**

Run: `dotnet build seq-mcp.sln -c Release`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/SeqMcp/SeqClient.fs
git commit -m "Add alert endpoints to SeqClient"
```

---

### Task 3: Alert renderers

**Files:**
- Modify: `src/SeqMcp/Render.fs` (append after the `signals` function, end of module)

**Interfaces:**
- Consumes: `Alert`, `AlertState` (Task 1); existing `cell`, `maxRows`, `StringBuilder`.
- Produces:
  - `Render.alerts : string -> Alert[] -> string`
  - `Render.alertDetail : Alert -> string`
  - `Render.alertState : Map<string,string> -> AlertState[] -> string`

- [ ] **Step 1: Append the three renderers to `Render.fs`**

```fsharp
    /// Render alerts as `Id · Title` lines (with a `· [disabled]` marker),
    /// filtered by an optional title substring. Mirrors `signals`.
    let alerts (nameFilter: string) (items: Alert[]) =
        let matches =
            if isNull (box items) then
                [||]
            elif String.IsNullOrWhiteSpace nameFilter then
                items
            else
                items
                |> Array.filter (fun a ->
                    not (isNull a.Title)
                    && a.Title.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)

        if matches.Length = 0 then
            "No alerts."
        else
            let sb = StringBuilder()

            for a in Array.truncate 50 matches do
                let disabled = if a.IsDisabled then " · [disabled]" else ""
                sb.AppendLine(sprintf "%s · %s%s" a.Id a.Title disabled) |> ignore

            if matches.Length > 50 then
                sb.AppendLine(sprintf "… %d more (use a name filter)" (matches.Length - 50)) |> ignore

            sb.ToString().TrimEnd()

    /// Render a single alert in full: header, owner/shared, signals, channels.
    let alertDetail (a: Alert) =
        let sb = StringBuilder()
        let disabled = if a.IsDisabled then " · [disabled]" else ""
        sb.AppendLine(sprintf "%s · %s%s" a.Id a.Title disabled) |> ignore

        let owner = if isNull a.OwnerId then "(none)" else a.OwnerId
        sb.AppendLine(sprintf "Shared: %b · Owner: %s" a.IsShared owner) |> ignore

        let sigs = if isNull (box a.Signals) then [||] else a.Signals

        if sigs.Length > 0 then
            sb.AppendLine(sprintf "Signals (%d):" sigs.Length) |> ignore

            for s in sigs do
                sb.AppendLine(sprintf "  %s" (cell s)) |> ignore

        let channels =
            if isNull (box a.NotificationChannels) then [||] else a.NotificationChannels

        if channels.Length > 0 then
            sb.AppendLine(sprintf "Notification channels (%d):" channels.Length) |> ignore

            for c in channels do
                sb.AppendLine(sprintf "  %s" (cell c)) |> ignore

        sb.ToString().TrimEnd()

    /// Render alert runtime state as `Id · Title · Status · Occurrences · Since`
    /// lines. `titles` maps alert id → title (from the alert definitions).
    let alertState (titles: Map<string, string>) (states: AlertState[]) =
        if isNull (box states) || states.Length = 0 then
            "No alerts firing."
        else
            states
            |> Array.map (fun s ->
                let key = if String.IsNullOrWhiteSpace s.AlertId then s.Id else s.AlertId
                let title = Map.tryFind key titles |> Option.defaultValue key
                let status = if isNull s.Status then "" else s.Status
                let since = if isNull s.FirstOccurrence then "" else s.FirstOccurrence
                sprintf "%s · %s · %s · %d · %s" key title status s.Occurrences since)
            |> String.concat "\n"
```

- [ ] **Step 2: Build**

Run: `dotnet build seq-mcp.sln -c Release`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/SeqMcp/Render.fs
git commit -m "Add alert renderers"
```

---

### Task 4: Commands core

**Files:**
- Modify: `src/SeqMcp/Commands.fs` (append after `listSignals`, end of module)

**Interfaces:**
- Consumes: `SeqClient.ListAlertsAsync/GetAlertAsync/AlertStateAsync` (Task 2); `Render.alerts/alertDetail/alertState` (Task 3).
- Produces:
  - `Commands.listAlerts : SeqClient -> string -> Task<string>`
  - `Commands.getAlert : SeqClient -> string -> Task<string>`
  - `Commands.alertState : SeqClient -> Task<string>`

- [ ] **Step 1: Append the three command functions to `Commands.fs`**

```fsharp
/// List configured alerts as Id · Title lines, optionally filtered by title substring.
let listAlerts (client: SeqClient) (nameFilter: string) : Task<string> =
    task {
        let! alerts = client.ListAlertsAsync()
        return Render.alerts nameFilter alerts
    }

/// Full detail for a single alert by its id.
let getAlert (client: SeqClient) (id: string) : Task<string> =
    task {
        let! a = client.GetAlertAsync id
        return Render.alertDetail a
    }

/// Current firing state of alerts, joined with alert titles by id. Fetches both
/// the state and the definitions so opaque state ids render with their titles.
let alertState (client: SeqClient) : Task<string> =
    task {
        let! states = client.AlertStateAsync()
        let! alerts = client.ListAlertsAsync()

        let titles =
            if isNull (box alerts) then
                Map.empty
            else
                alerts
                |> Array.choose (fun a -> if isNull a.Id then None else Some(a.Id, a.Title))
                |> Map.ofArray

        return Render.alertState titles states
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build seq-mcp.sln -c Release`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/SeqMcp/Commands.fs
git commit -m "Add alert commands to shared core"
```

---

### Task 5: MCP tool front-end

**Files:**
- Modify: `src/SeqMcp/Tools.fs` (add three members after `ListSignals`, end of type)

**Interfaces:**
- Consumes: `Commands.listAlerts/getAlert/alertState` (Task 4); existing private `run`.
- Produces: MCP tools `list_alerts`, `get_alert`, `alert_state`.

- [ ] **Step 1: Add the three tool members to `Tools.fs`**

```fsharp
    [<McpServerTool; Description("List configured Seq alerts as Id · Title lines (with a [disabled] marker for disabled alerts). Pass a name filter to narrow a large list; the returned Id can be passed to get_alert for full detail.")>]
    member _.ListAlerts
        (
            [<Description("Optional case-insensitive substring to filter alert titles."); Optional; DefaultParameterValue(null: string)>] nameFilter: string
        ) : Task<string> =
        run (fun () -> Commands.listAlerts client nameFilter)

    [<McpServerTool; Description("Get full detail for a single alert by its id (the Id from list_alerts): title, enabled/disabled, owner, shared status, signals, and notification channels.")>]
    member _.GetAlert([<Description("The alert id, e.g. 'alert-123'")>] id: string) : Task<string> =
        run (fun () -> Commands.getAlert client id)

    [<McpServerTool; Description("List the current firing state of Seq alerts as Id · Title · Status · Occurrences · Since lines, newest state joined with alert titles. Note: may require an API key with the Project permission.")>]
    member _.AlertState() : Task<string> =
        run (fun () -> Commands.alertState client)
```

- [ ] **Step 2: Build**

Run: `dotnet build seq-mcp.sln -c Release`
Expected: `Build succeeded`.

- [ ] **Step 3: Verify the tools are registered over stdio**

Run (PowerShell): pipe an MCP `tools/list` request to the stdio server and confirm the three new tools appear.

```pwsh
$req = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}' + "`n" + '{"jsonrpc":"2.0","method":"notifications/initialized"}' + "`n" + '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
$req | dotnet run --project src/SeqMcp -c Release -- --stdio
```

Expected: the JSON response to id `2` lists tool names including `list_alerts`, `get_alert`, and `alert_state` alongside the existing five.

- [ ] **Step 4: Commit**

```bash
git add src/SeqMcp/Tools.fs
git commit -m "Expose alert tools over MCP"
```

---

### Task 6: CLI front-end + live verification

**Files:**
- Modify: `src/SeqMcp/Cli.fs` (add three entries to `specs`; add three arms to `dispatch`)
- Possibly modify: `src/SeqMcp/Models.fs` (only if live JSON field names differ)

**Interfaces:**
- Consumes: `Commands.listAlerts/getAlert/alertState` (Task 4).
- Produces: CLI verbs `alerts`, `alert`, `alert-state` (also surfaced by `seq-mcp commands` and `--help`).

- [ ] **Step 1: Add three `CliSpec` entries to the `specs` list in `Cli.fs`**

Insert as the last three elements of the `specs` list (after the `signals` entry, inside the `[ … ]`):

```fsharp
      { name = "alerts"
        summary = "List configured alerts as Id · Title (with a [disabled] marker)."
        args = []
        opts = [ opt "filter" "Case-insensitive title substring" null ] }
      { name = "alert"
        summary = "Full detail for a single alert by its id."
        args = [ "id" ]
        opts = [] }
      { name = "alert-state"
        summary = "Current firing state of alerts (Status · Occurrences · Since)."
        args = []
        opts = [] } ]
```

Note: this replaces the `] }` that currently closes the `signals` entry and the list — the `signals` entry keeps its own closing `}`, then the three new entries follow, and the list `]` moves to the end of the `alert-state` entry as shown.

- [ ] **Step 2: Add three arms to the `dispatch` match in `Cli.fs`**

Insert before the final `| _ -> None`:

```fsharp
    | "alerts" -> Some(Commands.listAlerts client (flag "filter"))
    | "alert" -> Some(Commands.getAlert client (arg 0))
    | "alert-state" -> Some(Commands.alertState client)
```

- [ ] **Step 3: Build**

Run: `dotnet build seq-mcp.sln -c Release`
Expected: `Build succeeded`.

- [ ] **Step 4: Verify the verbs are discoverable**

Run: `dotnet run --project src/SeqMcp -c Release -- --help`
Expected: output lists `alerts`, `alert`, and `alert-state` with their summaries.

Run: `dotnet run --project src/SeqMcp -c Release -- commands`
Expected: JSON array includes the three new command objects.

- [ ] **Step 5: Live-verify against Seq and confirm field names**

Ensure `SEQ__SERVERURL` (and `SEQ__APIKEY` if required) are set in the shell, then:

```pwsh
dotnet run --project src/SeqMcp -c Release -- alerts
dotnet run --project src/SeqMcp -c Release -- alert-state
```

Expected `alerts`: one `Id · Title` line per configured alert (or `No alerts.`), with `[disabled]` where applicable. Take an `Id` from that output and run:

```pwsh
dotnet run --project src/SeqMcp -c Release -- alert <id-from-above>
```

Expected `alert <id>`: the detail block (header, `Shared:`/`Owner:`, signals, channels).

**Field-name check:** if any field renders empty/wrong (e.g. every alert shows a blank title, or `alert-state` shows only ids where titles were expected), the live JSON casing differs from the model. Inspect the raw payload:

```pwsh
curl.exe -H "X-Seq-ApiKey: $env:SEQ__APIKEY" "$env:SEQ__SERVERURL/api/alerts?shared=true"
curl.exe -H "X-Seq-ApiKey: $env:SEQ__APIKEY" "$env:SEQ__SERVERURL/api/alertstate"
```

Adjust the `[<JsonPropertyName(...)>]` attributes in `Models.fs` (Task 1) to match the real names, rebuild, and re-run this step. In particular confirm the `alertstate` join key: whether the alert id lives on `AlertId` or on the state entity's own `Id`.

**Permission check:** if `alert-state` prints an "unauthorized" message, the API key lacks the `Project` permission — this is the documented graceful-degradation path, not a bug. Note it and continue.

- [ ] **Step 6: Commit**

```bash
git add src/SeqMcp/Cli.fs src/SeqMcp/Models.fs
git commit -m "Add alert verbs to CLI and confirm live field names"
```

---

### Task 7: Documentation

**Files:**
- Modify: `README.md` (Tools table ~lines 10-16; CLI examples ~lines 42-47)

**Interfaces:**
- Consumes: nothing (docs).
- Produces: nothing consumed downstream.

- [ ] **Step 1: Add three rows to the Tools table in `README.md`**

After the `list_signals` row, add:

```markdown
| `list_alerts(nameFilter?)` | Configured alerts as `Id · Title` lines (with a `[disabled]` marker). Pass a name substring to narrow; the `Id` can be passed to `get_alert`. |
| `get_alert(id)` | Full detail for one alert: title, enabled/disabled, owner, shared status, signals, and notification channels. |
| `alert_state()` | Current firing state of alerts as `Id · Title · Status · Occurrences · Since` lines. May require an API key with the `Project` permission. |
```

- [ ] **Step 2: Add three CLI examples in `README.md`**

In the CLI fenced block (after the `seq-mcp signals --filter checkout` line), add:

```sh
seq-mcp alerts --filter checkout
seq-mcp alert alert-123
seq-mcp alert-state
```

- [ ] **Step 3: Build (docs are packed into the tool)**

Run: `dotnet build seq-mcp.sln -c Release`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "Document alert tools and CLI verbs"
```

---

## Self-Review Notes

- **Spec coverage:** `list_alerts` (Task 4/5/6), `get_alert` (Task 4/5/6), `alert_state` with title join (Task 4), models (Task 1), client (Task 2), rendering incl. empty-result strings (Task 3), MCP + CLI front-ends and `Cli.specs` sync (Tasks 5-6), `Project`-permission caveat (Tasks 5-6 verification + Task 7 docs), README (Task 7), live field-name confirmation (Task 6 Step 5). All spec sections mapped.
- **Type consistency:** `Alert`/`AlertState` shapes and the `listAlerts`/`getAlert`/`alertState` / `ListAlertsAsync`/`GetAlertAsync`/`AlertStateAsync` names are identical across Tasks 1-6. Renderer `alertState` takes `Map<string,string>` in both Task 3 and Task 4.
- **No placeholders:** every code step contains complete F#; no TBD/TODO.
