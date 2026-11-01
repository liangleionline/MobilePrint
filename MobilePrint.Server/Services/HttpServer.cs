using System.Net;
using System.Text;
using System.Text.Json;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using MobilePrint.Server.Models;
using MobilePrint.Server.Services;

namespace MobilePrint.Server.Services;

public class HttpServer
{
    private readonly ArchiveManager _archive;
    private readonly TaskQueue _queue;
    private readonly PdfRenderer _renderer;
    private readonly AppConfig _config;
    private WebApplication? _app;

    public event Action? OnStarted;
    public event Action? OnStopped;

    public bool IsRunning => _app != null;

    public HttpServer(ArchiveManager archive, TaskQueue queue, PdfRenderer renderer, AppConfig config)
    {
        _archive = archive;
        _queue = queue;
        _renderer = renderer;
        _config = config;
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = 100 * 1024 * 1024;
            k.ListenAnyIP(_config.ListenPort);
        });

        _app = builder.Build();

        // auth middleware
        _app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                string? auth = context.Request.Headers.Authorization.FirstOrDefault();
                string expected = $"Bearer {_config.AuthToken}";
                if (auth != expected)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("{\"code\":401,\"msg\":\"未授权\"}");
                    return;
                }
            }
            await next();
        });

        // heartbeat
        _app.MapGet("/api/heartbeat", () => Results.Json(new { code = 200, msg = "ok", data = new { port = _config.ListenPort, printer = _config.PrinterName } }));

        // upload
        _app.MapPost("/api/print/upload", async (HttpRequest req) =>
        {
            try
            {
                var form = await req.ReadFormAsync();
                var file = form.Files.FirstOrDefault();
                if (file == null) return Results.BadRequest(new { code = 400, msg = "未收到文件" });

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                byte[] data = ms.ToArray();

                var p = new PrintParameters
                {
                    Copies = int.TryParse(form["copies"], out var c) ? c : 1,
                    Orientation = form["orientation"] == "Landscape" ? Orientation.Landscape : Orientation.Portrait,
                    PageRange = form["pageRange"].ToString(),
                    ScaleMode = form["scaleMode"].ToString() switch { "Original" => ScaleMode.Original, "Custom" => ScaleMode.Custom, _ => ScaleMode.FitA4 },
                    ScalePercent = int.TryParse(form["scalePercent"], out var sp) ? sp : 100,
                    MarginTop = int.TryParse(form["marginTop"], out var mt) ? mt : 10,
                    MarginBottom = int.TryParse(form["marginBottom"], out var mb) ? mb : 10,
                    MarginLeft = int.TryParse(form["marginLeft"], out var ml) ? ml : 10,
                    MarginRight = int.TryParse(form["marginRight"], out var mr) ? mr : 10,
                };

                var task = _queue.EnqueueUploadedFile(data, file.FileName, p, out string fileId, out int totalPages);
                return Results.Json(new { code = 200, msg = "上传成功", data = new { taskId = task.TaskId, fileId, totalPage = totalPages } });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { code = 500, msg = ex.Message });
            }
        });

        // task status
        _app.MapGet("/api/print/status/{taskId}", (string taskId) =>
        {
            var t = _queue.GetTask(taskId);
            if (t == null) return Results.NotFound(new { code = 404, msg = "任务不存在" });
            return Results.Json(new
            {
                code = 200,
                data = new
                {
                    taskId = t.TaskId,
                    status = t.Status.ToString(),
                    fileName = t.FileName,
                    failReason = t.FailReason,
                    addedTime = t.AddedTime,
                    finishedTime = t.FinishedTime,
                    totalPages = t.TotalPages
                }
            });
        });

        // cancel
        _app.MapPost("/api/print/cancel/{taskId}", (string taskId) =>
        {
            bool ok = _queue.CancelTask(taskId);
            return Results.Json(new { code = ok ? 200 : 500, msg = ok ? "取消成功" : "打印机已开始打印，无法终止" });
        });

        // reprint
        _app.MapPost("/api/print/reprint/{fileId}", async (string fileId, HttpRequest req) =>
        {
            try
            {
                var form = await req.ReadFormAsync();
                var p = new PrintParameters
                {
                    Copies = int.TryParse(form["copies"], out var c) ? c : 1,
                    Orientation = form["orientation"] == "Landscape" ? Orientation.Landscape : Orientation.Portrait,
                    PageRange = form["pageRange"].ToString(),
                    ScaleMode = form["scaleMode"].ToString() switch { "Original" => ScaleMode.Original, "Custom" => ScaleMode.Custom, _ => ScaleMode.FitA4 },
                    ScalePercent = int.TryParse(form["scalePercent"], out var sp) ? sp : 100,
                    MarginTop = int.TryParse(form["marginTop"], out var mt) ? mt : 10,
                    MarginBottom = int.TryParse(form["marginBottom"], out var mb) ? mb : 10,
                    MarginLeft = int.TryParse(form["marginLeft"], out var ml) ? ml : 10,
                    MarginRight = int.TryParse(form["marginRight"], out var mr) ? mr : 10,
                };
                var task = _queue.Reprint(fileId, p);
                return Results.Json(new { code = 200, data = new { taskId = task.TaskId } });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { code = 500, msg = ex.Message });
            }
        });

        // preview
        _app.MapGet("/api/preview/render", (string fileId, int targetPage, string? orientation, string? pageRange, string? scaleMode, int? scalePercent, int? marginTop, int? marginBottom, int? marginLeft, int? marginRight) =>
        {
            try
            {
                var t = _queue.GetByFileId(fileId);
                if (t == null) return Results.NotFound("文件不存在");

                var p = new PrintParameters
                {
                    Orientation = orientation == "Landscape" ? Orientation.Landscape : Orientation.Portrait,
                    PageRange = pageRange ?? "",
                    ScaleMode = scaleMode switch { "Original" => ScaleMode.Original, "Custom" => ScaleMode.Custom, _ => ScaleMode.FitA4 },
                    ScalePercent = scalePercent ?? 100,
                    MarginTop = marginTop ?? 10,
                    MarginBottom = marginBottom ?? 10,
                    MarginLeft = marginLeft ?? 10,
                    MarginRight = marginRight ?? 10,
                };

                string ext = Path.GetExtension(t.FilePath).ToLower();
                byte[] jpeg;
                if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".webp" or ".gif")
                {
                    jpeg = _renderer.RenderImagePreviewJpeg(t.FilePath, p);
                }
                else
                {
                    string pdfPath = t.PdfPath ?? t.FilePath;
                    if (!File.Exists(pdfPath)) return Results.NotFound("PDF未就绪");
                    jpeg = _renderer.RenderPreviewJpeg(pdfPath, targetPage - 1, p);
                }
                return Results.File(jpeg, "image/jpeg");
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ex.Message);
            }
        });

        await _app.StartAsync();
        OnStarted?.Invoke();
    }

    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            _app = null;
        }
        OnStopped?.Invoke();
    }
}
