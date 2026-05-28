using System.Text.Json;
using GeminiProxyServer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("ChromeExtension", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<GeminiService>();

var app = builder.Build();

app.UseCors("ChromeExtension");

// ── Health ───────────────────────────────────────────────────────────────────

app.MapGet("/", () => Results.Ok(new { status = "GeminiProxy running", version = "1.0" }));

app.MapGet("/api/health", async (GeminiService gemini) =>
{
    var ok = await gemini.HealthCheckAsync();
    return Results.Ok(new { geminiCliAvailable = ok });
});

// ── Text chat ────────────────────────────────────────────────────────────────

app.MapPost("/api/chat", async (ChatRequest req, GeminiService gemini) =>
{
    try
    {
        var result = await gemini.ChatAsync(req.Message, req.SystemPrompt);
        return Results.Ok(new { reply = result });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// ── Schema fill (natural language → structured JSON for WebForms) ────────────

app.MapPost("/api/schema-fill", async (SchemaFillRequest req, GeminiService gemini) =>
{
    try
    {
        var result = await gemini.SchemaFillAsync(req.Instruction, req.Schema, req.Context);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
});

// ── Invoice / document file upload → structured JSON ────────────────────────

app.MapPost("/api/parse-invoice", async (HttpRequest httpReq, GeminiService gemini) =>
{
    if (!httpReq.HasFormContentType)
        return Results.BadRequest(new { error = "multipart/form-data required" });

    var form = await httpReq.ReadFormAsync();
    var schemaType = form["schemaType"].FirstOrDefault() ?? "uriage";
    var instruction = form["instruction"].FirstOrDefault() ?? "";

    string? imagePath = null;
    if (form.Files.Count > 0)
    {
        var file = form.Files[0];
        var ext = Path.GetExtension(file.FileName);
        imagePath = Path.Combine(Path.GetTempPath(), $"gemini_upload_{Guid.NewGuid()}{ext}");
        await using var fs = File.Create(imagePath);
        await file.CopyToAsync(fs);
    }

    try
    {
        var result = await gemini.ParseInvoiceAsync(imagePath, schemaType, instruction);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 500);
    }
    finally
    {
        if (imagePath != null && File.Exists(imagePath))
            File.Delete(imagePath);
    }
}).DisableAntiforgery();

// ── SSE streaming chat (for long-running Gemini calls) ───────────────────────

app.MapPost("/api/chat/stream", async (ChatRequest req, GeminiService gemini, HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    async Task Send(string evt, string data)
    {
        await ctx.Response.WriteAsync($"event: {evt}\ndata: {data}\n\n");
        await ctx.Response.Body.FlushAsync();
    }

    try
    {
        await Send("start", JsonSerializer.Serialize(new { message = "Processing..." }));
        var result = await gemini.ChatAsync(req.Message, req.SystemPrompt);
        await Send("reply", JsonSerializer.Serialize(new { text = result }));
        await Send("done", "{}");
    }
    catch (Exception ex)
    {
        await Send("error", JsonSerializer.Serialize(new { message = ex.Message }));
    }
});

// ── SSE streaming invoice parse ───────────────────────────────────────────────

app.MapPost("/api/parse-invoice/stream", async (HttpRequest httpReq, GeminiService gemini, HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    ctx.Response.Headers.Connection = "keep-alive";

    async Task Send(string evt, string data)
    {
        await ctx.Response.WriteAsync($"event: {evt}\ndata: {data}\n\n");
        await ctx.Response.Body.FlushAsync();
    }

    if (!httpReq.HasFormContentType)
    {
        await Send("error", JsonSerializer.Serialize(new { message = "multipart/form-data required" }));
        return;
    }

    var form = await httpReq.ReadFormAsync();
    var schemaType = form["schemaType"].FirstOrDefault() ?? "uriage";
    var instruction = form["instruction"].FirstOrDefault() ?? "";

    string? imagePath = null;
    if (form.Files.Count > 0)
    {
        var file = form.Files[0];
        var ext = Path.GetExtension(file.FileName);
        imagePath = Path.Combine(Path.GetTempPath(), $"gemini_stream_{Guid.NewGuid()}{ext}");
        await using var fs = File.Create(imagePath);
        await file.CopyToAsync(fs);
    }

    try
    {
        await Send("start", JsonSerializer.Serialize(new { message = "Gemini解析中..." }));
        var result = await gemini.ParseInvoiceAsync(imagePath, schemaType, instruction);
        await Send("result", JsonSerializer.Serialize(result));
        await Send("done", "{}");
    }
    catch (Exception ex)
    {
        await Send("error", JsonSerializer.Serialize(new { message = ex.Message }));
    }
    finally
    {
        if (imagePath != null && File.Exists(imagePath))
            File.Delete(imagePath);
    }
}).DisableAntiforgery();

app.Run();

// ── Request models ────────────────────────────────────────────────────────────

record ChatRequest(string Message, string? SystemPrompt = null);
record SchemaFillRequest(string Instruction, string Schema, string? Context = null);
