module SeqMcp.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open SeqMcp

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    let config = builder.Configuration

    let serverUrl =
        match config.["Seq:ServerUrl"] with
        | null
        | "" -> "http://localhost:5341"
        | u -> u

    let apiKey = config.["Seq:ApiKey"]

    builder.Services.AddHttpClient<SeqClient>(fun http ->
        http.BaseAddress <- Uri(serverUrl.TrimEnd('/') + "/")

        if not (String.IsNullOrWhiteSpace apiKey) then
            http.DefaultRequestHeaders.Add("X-Seq-ApiKey", apiKey))
    |> ignore

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<SeqTools>()
    |> ignore

    let app = builder.Build()
    app.MapMcp() |> ignore

    // Default the listen address to :5250 for local/dev use; respect an explicit
    // ASPNETCORE_URLS when the host sets one.
    match Environment.GetEnvironmentVariable "ASPNETCORE_URLS" with
    | null
    | "" -> app.Run "http://localhost:5250"
    | _ -> app.Run()

    0
