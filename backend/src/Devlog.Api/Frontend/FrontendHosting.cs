using Devlog.Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.FileProviders;

namespace Devlog.Api.Frontend;

/// <summary>
/// Serves the built React dashboard from the collector itself, so the shipped
/// app and the API share one origin and one process.
/// </summary>
public static class FrontendHosting
{
    /// <summary>
    /// Where <c>npm run build</c> lands, relative to the running executable.
    /// Resolved from <see cref="AppContext.BaseDirectory"/> rather than
    /// <c>IWebHostEnvironment.WebRootPath</c>: this process uses the Worker SDK,
    /// which does not populate that property the way a web project would.
    /// </summary>
    private static string WebRoot => Path.Combine(AppContext.BaseDirectory, "wwwroot");

    public static WebApplication MapDevlogFrontend(this WebApplication app)
    {
        var indexPath = Path.Combine(WebRoot, "index.html");

        // A checkout that has never run `npm run build` is the normal state of a
        // fresh clone, so say what to do rather than answering 404 and looking
        // like the API is broken.
        if (!File.Exists(indexPath))
        {
            app.MapFallback(() => Results.Text(
                "devlog is running, but the dashboard has not been built yet.\n\n"
                + "  cd frontend && npm ci && npm run build\n\n"
                + "Then rebuild the collector so the files are copied next to the exe.\n",
                "text/plain"));

            return app;
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(WebRoot)
        });

        app.MapFallback(async (HttpContext context, ApiTokenStore tokens) =>
        {
            // Unmatched API paths must not answer with the dashboard. Without
            // this, GET /v1/typo returns a page carrying the token instead of
            // the 404 the caller asked a question of.
            var path = context.Request.Path.Value ?? "/";
            if (path.StartsWith("/v1", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";

            await context.Response.WriteAsync(Inject(await File.ReadAllTextAsync(indexPath), tokens.Token));
        });

        return app;
    }

    /// <summary>
    /// Puts the API token into the page as a global, which is what
    /// <c>frontend/src/api/client.ts</c> reads.
    /// <para>
    /// It goes into the HTML and nowhere else. Serving it from its own
    /// <c>.js</c> endpoint would be tidier and is a real vulnerability: classic
    /// <c>&lt;script src&gt;</c> inclusion is not subject to CORS, so any page
    /// on the internet could pull that file and have the token defined on its
    /// own window. Reading an HTML response cross-origin <i>is</i> blocked,
    /// which is the whole reason this form is safe. Loopback stops other
    /// computers, not other websites -- the token is what stops those.
    /// </para>
    /// </summary>
    private static string Inject(string html, string token)
    {
        // A classic inline script runs during parsing; Vite's bundle is a module
        // and therefore deferred. So this is set before any app code reads it,
        // even though it sits after the bundle's tag in document order.
        var script = $"<script>window.__DEVLOG_TOKEN__={System.Text.Json.JsonSerializer.Serialize(token)}</script>";

        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);

        return head < 0
            ? script + html
            : html[..head] + script + html[head..];
    }
}
