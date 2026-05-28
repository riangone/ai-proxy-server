using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GeminiProxyServer;

public class GeminiService(ILogger<GeminiService> logger, IConfiguration config)
{
    private readonly string _geminiPath = config["Gemini:CliPath"] ?? "gemini";

    private static readonly Dictionary<string, string> BuiltinSchemas = new()
    {
        ["uriage"] = """
            {
              "tokuisakiCode": "string (得意先コード)",
              "denpyoDate": "string (YYYY-MM-DD)",
              "meisai": [
                {
                  "shohinCode": "string",
                  "shohinName": "string",
                  "suryo": "number",
                  "tanka": "number",
                  "kingaku": "number"
                }
              ],
              "gokei": "number"
            }
            """,
        ["mitsumori"] = """
            {
              "tokuisakiCode": "string",
              "mitsumoriDate": "string (YYYY-MM-DD)",
              "yukoKigen": "string (YYYY-MM-DD)",
              "meisai": [
                {
                  "shohinCode": "string",
                  "shohinName": "string",
                  "suryo": "number",
                  "tanka": "number",
                  "kingaku": "number"
                }
              ],
              "gokei": "number"
            }
            """,
        ["seikyu"] = """
            {
              "tokuisakiCode": "string",
              "seikyuDate": "string (YYYY-MM-DD)",
              "shimekiriDate": "string (YYYY-MM-DD)",
              "seikyuGaku": "number",
              "zeiritsu": "number (例: 0.10)",
              "biko": "string"
            }
            """
    };

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var result = await RunGeminiAsync("Say OK in one word.", timeoutSeconds: 15);
            return !string.IsNullOrEmpty(result);
        }
        catch
        {
            return false;
        }
    }

    public Task<string> ChatAsync(string message, string? systemPrompt)
    {
        var prompt = systemPrompt != null
            ? $"[System]\n{systemPrompt}\n\n[User]\n{message}"
            : message;
        return RunGeminiAsync(prompt);
    }

    public async Task<object> SchemaFillAsync(string instruction, string schema, string? context)
    {
        var prompt = BuildSchemaPrompt(instruction, schema, context);
        var raw = await RunGeminiAsync(prompt);
        return ExtractJson(raw);
    }

    public async Task<object> ParseInvoiceAsync(string? imagePath, string schemaType, string instruction)
    {
        var schema = BuiltinSchemas.GetValueOrDefault(schemaType, BuiltinSchemas["uriage"]);
        var hint = string.IsNullOrWhiteSpace(instruction)
            ? "この伝票・請求書・見積書の内容を解析してください。"
            : instruction;

        string prompt;
        string[]? extraArgs = null;

        if (imagePath != null && File.Exists(imagePath))
        {
            // Prefer @filename syntax first; fall back to base64 inline if file is small enough
            var fileInfo = new FileInfo(imagePath);
            if (fileInfo.Length <= 4 * 1024 * 1024) // ≤4 MB: embed as base64
            {
                var bytes = await File.ReadAllBytesAsync(imagePath);
                var b64 = Convert.ToBase64String(bytes);
                var mime = GetMimeType(imagePath);
                var dataUri = $"data:{mime};base64,{b64}";
                prompt = BuildSchemaPrompt(
                    $"{hint} 添付画像から日本語テキストを正確に読み取ってください。\n\n[添付ファイル (base64)]: {dataUri}",
                    schema, null);
            }
            else
            {
                extraArgs = [imagePath];
                prompt = BuildSchemaPrompt($"{hint} 画像から日本語テキストを正確に読み取ってください。", schema, null);
            }
        }
        else
        {
            prompt = BuildSchemaPrompt(hint, schema, null);
        }

        var raw = await RunGeminiAsync(prompt, extraArgs: extraArgs);
        return ExtractJson(raw);
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    // ── Core runner ─────────────────────────────────────────────────────────

    private async Task<string> RunGeminiAsync(
        string prompt,
        int timeoutSeconds = 60,
        string[]? extraArgs = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _geminiPath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("--yolo");
        if (extraArgs != null)
            foreach (var a in extraArgs) psi.ArgumentList.Add(a);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process { StartInfo = psi };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Gemini CLI timed out after {timeoutSeconds}s");
        }

        var output = stdout.ToString().Trim();
        if (string.IsNullOrEmpty(output) && stderr.Length > 0)
            logger.LogWarning("Gemini stderr: {Err}", stderr);

        return output;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string BuildSchemaPrompt(string instruction, string schema, string? context)
    {
        var sb = new StringBuilder();
        sb.AppendLine("あなたは日本の販売管理システムのデータ解析アシスタントです。");
        sb.AppendLine("必ず有効なJSONのみを出力してください。説明文や```ブロックは不要です。");
        sb.AppendLine();
        sb.AppendLine($"[指示]\n{instruction}");
        sb.AppendLine();
        sb.AppendLine($"[出力スキーマ]\n{schema}");
        if (!string.IsNullOrWhiteSpace(context))
        {
            sb.AppendLine();
            sb.AppendLine($"[追加コンテキスト]\n{context}");
        }
        sb.AppendLine();
        sb.AppendLine("スキーマに厳密に従ったJSONを出力してください。不明な値はnullとしてください。");
        return sb.ToString();
    }

    private static object ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new { error = "Gemini returned empty response" };

        var cleaned = Regex.Replace(raw, @"```(?:json)?\s*", "").Replace("```", "").Trim();

        var start = cleaned.IndexOfAny(['{', '[']);
        if (start >= 0) cleaned = cleaned[start..];

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new { raw_response = raw, parse_error = "Could not parse as JSON" };
        }
    }
}
