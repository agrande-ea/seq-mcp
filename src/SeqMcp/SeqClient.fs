namespace SeqMcp

open System
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks

/// Typed wrapper over Seq's HTTP API. Base address and the X-Seq-ApiKey header
/// are configured in DI (see Program.fs). All tool capabilities are built on the
/// single, well-defined query endpoint: GET /api/data.
type SeqClient(http: HttpClient) =

    static let jsonOptions = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

    static let isoUtc (t: DateTime) =
        t.ToUniversalTime().ToString("o")

    /// Run a Seq SQL query over an optional [rangeStartUtc, rangeEndUtc) window,
    /// optionally scoped to a saved signal.
    member _.QueryAsync
        (
            sql: string,
            signal: string option,
            rangeStartUtc: DateTime option,
            rangeEndUtc: DateTime option
        ) : Task<QueryResult> =
        task {
            let parts =
                [ yield "q=" + Uri.EscapeDataString sql
                  match signal with
                  | Some s when not (String.IsNullOrWhiteSpace s) -> yield "signal=" + Uri.EscapeDataString s
                  | _ -> ()
                  match rangeStartUtc with
                  | Some t -> yield "rangeStartUtc=" + Uri.EscapeDataString(isoUtc t)
                  | None -> ()
                  match rangeEndUtc with
                  | Some t -> yield "rangeEndUtc=" + Uri.EscapeDataString(isoUtc t)
                  | None -> () ]

            let url = "api/data?" + String.Join("&", parts)

            use! resp = http.GetAsync url
            let! body = resp.Content.ReadAsStringAsync()

            if not resp.IsSuccessStatusCode then
                return failwithf "Seq returned %d: %s" (int resp.StatusCode) (body.Trim())
            else
                return JsonSerializer.Deserialize<QueryResult>(body, jsonOptions)
        }

    /// List rendered events matching a Seq filter expression, newest first, over an
    /// optional [from, to) window. Uses GET /api/events (purpose-built for event listing).
    member _.EventsAsync
        (
            filter: string,
            count: int,
            signal: string option,
            fromUtc: DateTime option,
            toUtc: DateTime option
        ) : Task<SeqEvent[]> =
        task {
            let parts =
                [ if not (String.IsNullOrWhiteSpace filter) then
                      yield "filter=" + Uri.EscapeDataString filter
                  match signal with
                  | Some s when not (String.IsNullOrWhiteSpace s) -> yield "signal=" + Uri.EscapeDataString s
                  | _ -> ()
                  yield "count=" + string count
                  yield "render=true"
                  match fromUtc with
                  | Some t -> yield "fromDateUtc=" + Uri.EscapeDataString(isoUtc t)
                  | None -> ()
                  match toUtc with
                  | Some t -> yield "toDateUtc=" + Uri.EscapeDataString(isoUtc t)
                  | None -> () ]

            let url = "api/events?" + String.Join("&", parts)

            use! resp = http.GetAsync url
            let! body = resp.Content.ReadAsStringAsync()

            if not resp.IsSuccessStatusCode then
                return failwithf "Seq returned %d: %s" (int resp.StatusCode) (body.Trim())
            else
                return JsonSerializer.Deserialize<SeqEvent[]>(body, jsonOptions)
        }

    /// Fetch a single event by id, with rendered message and full detail.
    member _.GetEventAsync(id: string) : Task<SeqEvent> =
        task {
            let url = "api/events/" + Uri.EscapeDataString id + "?render=true"
            use! resp = http.GetAsync url
            let! body = resp.Content.ReadAsStringAsync()

            if not resp.IsSuccessStatusCode then
                return failwithf "Seq returned %d: %s" (int resp.StatusCode) (body.Trim())
            else
                return JsonSerializer.Deserialize<SeqEvent>(body, jsonOptions)
        }

    /// List saved signals (shared and personal).
    member _.ListSignalsAsync() : Task<Signal[]> =
        task {
            use! resp = http.GetAsync "api/signals?shared=true"
            let! body = resp.Content.ReadAsStringAsync()

            if not resp.IsSuccessStatusCode then
                return failwithf "Seq returned %d: %s" (int resp.StatusCode) (body.Trim())
            else
                return JsonSerializer.Deserialize<Signal[]>(body, jsonOptions)
        }
