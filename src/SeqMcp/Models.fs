namespace SeqMcp

open System.Text.Json
open System.Text.Json.Serialization

/// Result of GET /api/data.
/// Only one of Rows / Slices / Series is populated for a given result set;
/// this server renders the tabular Rows shape (the one produced by plain
/// selects and `group by <expr>` aggregates).
type QueryResult =
    { [<JsonPropertyName("Columns")>]
      Columns: string[]

      [<JsonPropertyName("Rows")>]
      Rows: JsonElement[][]

      /// Populated instead of Rows when the query groups by time(...).
      [<JsonPropertyName("Slices")>]
      Slices: JsonElement

      /// Query-level error message, when the query could not be executed.
      [<JsonPropertyName("Error")>]
      Error: string

      [<JsonPropertyName("Reasons")>]
      Reasons: string[] }

/// A single property attached to an event (Name = Value).
type EventProperty =
    { [<JsonPropertyName("Name")>]
      Name: string

      [<JsonPropertyName("Value")>]
      Value: JsonElement }

/// A rendered event from GET /api/events (render=true) or /api/events/{id}.
/// Level is omitted by Seq for Information-level events; Exception/Properties
/// are populated for the single-event detail view.
type SeqEvent =
    { [<JsonPropertyName("Id")>]
      Id: string

      [<JsonPropertyName("Timestamp")>]
      Timestamp: string

      [<JsonPropertyName("Level")>]
      Level: string

      [<JsonPropertyName("RenderedMessage")>]
      RenderedMessage: string

      [<JsonPropertyName("Exception")>]
      Exception: string

      [<JsonPropertyName("Properties")>]
      Properties: EventProperty[] }

/// A saved Seq signal (a named, reusable filter) from GET /api/signals.
type Signal =
    { [<JsonPropertyName("Id")>]
      Id: string

      [<JsonPropertyName("Title")>]
      Title: string

      [<JsonPropertyName("Description")>]
      Description: string }

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
