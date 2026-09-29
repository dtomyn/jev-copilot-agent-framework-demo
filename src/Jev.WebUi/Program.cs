using System.Text.Json;
using Jev.WebUi;
using Microsoft.AspNetCore.Http.HttpResults;

// A presentation front end for the ten levels. It drives Jev.Core directly for everything that
// does not need a coding agent, runs the real hook executable for level 8, and shells out to the
// TenLevels.Jev CLI for the Copilot-backed levels.
//
// It deliberately does not reference TenLevels.Jev: that project downloads the Copilot runtime
// from registry.npmjs.org at build time, and the repository keeps it last in the build order so
// the hook and the self-tests stay buildable offline. Referencing it here would put an npm
// download in front of "show me the demo".

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Localhost only, and a fixed port, because this is a presenter tool that gets bookmarked, not a
// service. Nothing here authenticates, and the hook panel starts processes.
builder.WebHost.UseUrls("http://127.0.0.1:5088");

builder.Services.AddSingleton(new RepositoryContent(builder.Environment.ContentRootPath));
builder.Services.AddSingleton<TutorialNotes>();
builder.Services.AddSingleton<HookRunner>();
builder.Services.AddSingleton<LevelCliRunner>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never);

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/config", (RepositoryContent repository, HookRunner hook, TutorialNotes notes) => new ConfigResponse(
    DecisionSession.Statuses(),
    DecisionSession.DefaultProvider,
    DecisionSession.DefaultMode,
    hook.Available,
    repository.CliAvailable,
    notes.Available,
    repository.Root));

app.MapGet("/api/levels", (RepositoryContent repository, TutorialNotes notes) =>
    repository.Levels.Select(level => level with { Notes = notes.For(level.Number) }));

// The tutorial is served from here, not opened off the filesystem, so its per-level "Run live"
// links and this page's links back into it are same-origin and survive being sent to someone.
app.MapGet("/tutorial", (RepositoryContent repository) =>
    repository.Read(TutorialNotes.TutorialPath) is { } html
        ? Results.Content(html, "text/html; charset=utf-8")
        : Results.NotFound());

app.MapGet("/api/hook/samples", (RepositoryContent repository) => repository.HookSamples);

app.MapPost("/api/decide", async (DecideRequest request, CancellationToken cancellationToken) =>
    await Guarded(() => DemoEndpoints.DecideAsync(request, cancellationToken)));

app.MapPost("/api/gate", async (GateRequest request, CancellationToken cancellationToken) =>
    await Guarded(() => DemoEndpoints.GateAsync(request, cancellationToken)));

app.MapPost("/api/tool", async (ToolRequest request, CancellationToken cancellationToken) =>
    await Guarded(() => DemoEndpoints.ToolAsync(request, cancellationToken)));

app.MapPost("/api/hook/run", async (HookRequest request, HookRunner hook, CancellationToken cancellationToken) =>
    await Guarded(() => hook.RunAsync(request, cancellationToken)));

// Server-sent events rather than a request/response pair: a Copilot-backed level takes tens of
// seconds and the interesting part is watching the agent decide, not the final string.
app.MapGet("/api/cli/stream", async (
    HttpContext context,
    LevelCliRunner runner,
    int level,
    string? provider,
    string? mode,
    CancellationToken cancellationToken) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["X-Accel-Buffering"] = "no";

    async Task Emit(string kind, string text)
    {
        await context.Response.WriteAsync(
            $"data: {JsonSerializer.Serialize(new { kind, text })}\n\n",
            cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    try
    {
        await runner.StreamAsync(level, provider, mode, Emit, cancellationToken);
    }
    catch (DecisionSession.UnavailableException ex)
    {
        await Emit("error", ex.Message);
    }
    catch (OperationCanceledException)
    {
        // The browser closed the EventSource; the runner already killed the child process.
    }
});

app.Run();

// A bad provider/mode combination or an empty question list is a presenter mistake, not a server
// fault: it comes back as a 400 with the message the UI shows inline.
static async Task<Results<Ok<T>, BadRequest<ProblemView>>> Guarded<T>(Func<Task<T>> work)
{
    try
    {
        return TypedResults.Ok(await work());
    }
    catch (DecisionSession.UnavailableException ex)
    {
        return TypedResults.BadRequest(new ProblemView(ex.Message));
    }
    catch (HttpRequestException ex)
    {
        return TypedResults.BadRequest(new ProblemView(
            $"The live provider call failed: {ex.Message}"));
    }
    catch (TaskCanceledException)
    {
        return TypedResults.BadRequest(new ProblemView(
            "The live provider did not answer in time. A cold Laya container loads its checkpoint on the first request; try again, or switch to mock."));
    }
}

internal sealed record ProblemView(string Error);
