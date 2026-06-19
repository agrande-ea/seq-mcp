module SeqMcp.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open SeqMcp

/// Register the typed Seq HTTP client (base address + X-Seq-ApiKey) from configuration.
/// Shared by both the HTTP and stdio hosts.
let private configureSeqClient (services: IServiceCollection) (config: IConfiguration) =
    let serverUrl =
        match config.["Seq:ServerUrl"] with
        | null
        | "" -> "http://localhost:5341"
        | u -> u

    let apiKey = config.["Seq:ApiKey"]

    services.AddHttpClient<SeqClient>(fun http ->
        http.BaseAddress <- Uri(serverUrl.TrimEnd('/') + "/")

        if not (String.IsNullOrWhiteSpace apiKey) then
            http.DefaultRequestHeaders.Add("X-Seq-ApiKey", apiKey))
    |> ignore

/// stdio transport: the MCP client (e.g. Claude Code) launches this process and speaks
/// JSON-RPC over stdin/stdout. Logs MUST go to stderr so they don't corrupt the protocol
/// stream on stdout.
let private runStdio (args: string[]) =
    let builder = Host.CreateApplicationBuilder(args)

    builder.Logging.AddConsole(fun o -> o.LogToStandardErrorThreshold <- LogLevel.Trace)
    |> ignore

    configureSeqClient builder.Services builder.Configuration

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<SeqTools>()
    |> ignore

    builder.Build().Run()
    0

/// HTTP (Streamable HTTP) transport: long-running server the MCP client connects to by URL.
let private runHttp (args: string[]) =
    let builder = WebApplication.CreateBuilder(args)
    configureSeqClient builder.Services builder.Configuration

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

[<EntryPoint>]
let main args =
    // `--stdio` selects the stdio transport (for `claude mcp add ... -- seq-mcp --stdio`);
    // otherwise the server runs over HTTP.
    if Array.contains "--stdio" args then runStdio args else runHttp args
