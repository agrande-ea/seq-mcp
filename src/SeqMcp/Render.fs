namespace SeqMcp

open System
open System.Text
open System.Text.Json

/// Rendering helpers: turn Seq query results into terse plain text so the LLM
/// (or a CLI reader) spends minimal tokens. Never returns raw JSON.
/// Shared by the MCP tools (Tools.fs) and the CLI (Cli.fs) via Commands.fs.
module internal Render =

    let maxRows = 100

    let cell (e: JsonElement) =
        match e.ValueKind with
        | JsonValueKind.String -> e.GetString()
        | JsonValueKind.Null
        | JsonValueKind.Undefined -> ""
        | _ -> e.GetRawText()

    /// Render Columns + Rows as `a · b · c` lines with a header, capped.
    let table (cols: string[]) (rows: JsonElement[][]) =
        let cols = if isNull (box cols) then [||] else cols
        let rows = if isNull (box rows) then [||] else rows
        let sb = StringBuilder()

        if cols.Length > 0 then
            sb.AppendLine(String.Join(" · ", cols)) |> ignore

        for row in Array.truncate maxRows rows do
            sb.AppendLine(String.Join(" · ", Array.map cell row)) |> ignore

        if rows.Length > maxRows then
            sb.AppendLine(sprintf "… %d more rows (refine your query)" (rows.Length - maxRows))
            |> ignore

        if rows.Length = 0 then "No rows." else sb.ToString().TrimEnd()

    /// Format a QueryResult, surfacing query errors and time-sliced results clearly.
    let result (r: QueryResult) =
        if not (String.IsNullOrWhiteSpace r.Error) then
            sprintf "Query error: %s" (r.Error.Trim())
        elif isNull (box r.Rows) && r.Slices.ValueKind = JsonValueKind.Array then
            "Query returned time-sliced data (group by time(...)). Use a value grouping such as `group by @Level` for tabular output."
        else
            table r.Columns r.Rows

    let private maxMessageLength = 300

    let private level (e: SeqEvent) =
        if String.IsNullOrWhiteSpace e.Level then "Information" else e.Level

    let private oneLineMessage (m: string) =
        match m with
        | null -> ""
        | m ->
            let s = m.Replace("\r", " ").Replace("\n", " ").Trim()
            if s.Length > maxMessageLength then s.Substring(0, maxMessageLength) + "…" else s

    /// Render events as terse `Id · Timestamp · Level · Message` lines, newest first.
    /// The id can be passed to get_event for full detail.
    let events (es: SeqEvent[]) =
        if isNull (box es) || es.Length = 0 then
            "No events."
        else
            es
            |> Array.map (fun e ->
                sprintf "%s · %s · %s · %s" e.Id e.Timestamp (level e) (oneLineMessage e.RenderedMessage))
            |> String.concat "\n"

    /// Render a single event in full: header, message, exception, and properties.
    let eventDetail (e: SeqEvent) =
        let sb = StringBuilder()
        sb.AppendLine(sprintf "%s · %s · %s" e.Id e.Timestamp (level e)) |> ignore

        if not (isNull e.RenderedMessage) then
            sb.AppendLine(e.RenderedMessage.Trim()) |> ignore

        if not (String.IsNullOrWhiteSpace e.Exception) then
            sb.AppendLine().AppendLine("Exception:").AppendLine(e.Exception.TrimEnd()) |> ignore

        if not (isNull (box e.Properties)) && e.Properties.Length > 0 then
            sb.AppendLine().AppendLine("Properties:") |> ignore

            for p in e.Properties do
                sb.AppendLine(sprintf "  %s = %s" p.Name (cell p.Value)) |> ignore

        sb.ToString().TrimEnd()

    /// Render signals as `Id · Title` lines, filtered by an optional title substring.
    let signals (nameFilter: string) (sigs: Signal[]) =
        let matches =
            if isNull (box sigs) then
                [||]
            elif String.IsNullOrWhiteSpace nameFilter then
                sigs
            else
                sigs
                |> Array.filter (fun s ->
                    not (isNull s.Title)
                    && s.Title.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)

        if matches.Length = 0 then
            "No matching signals."
        else
            let sb = StringBuilder()

            for s in Array.truncate 50 matches do
                sb.AppendLine(sprintf "%s · %s" s.Id s.Title) |> ignore

            if matches.Length > 50 then
                sb.AppendLine(sprintf "… %d more (use a name filter)" (matches.Length - 50)) |> ignore

            sb.ToString().TrimEnd()
