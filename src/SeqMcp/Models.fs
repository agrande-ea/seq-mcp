namespace SeqMcp

open System.Text.Json
open System.Text.Json.Serialization

/// Result of GET /api/data/query.
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
