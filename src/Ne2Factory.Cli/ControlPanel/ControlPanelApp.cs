using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Ne2Factory.Cli.ControlPanel.Endpoints;

namespace Ne2Factory.Cli.ControlPanel;

// The panel itself: a static single-page app (wwwroot) over a JSON API under
// /api, one Endpoints/*Endpoints class per area. Split from ControlPanelCommand,
// which hosts it, so tests can mount it on a test server.
internal static class ControlPanelApp
{
    // Only the panel's own page sends it: a cross-site page can't without a CORS
    // preflight, which the panel never allows. Required on anything but reads.
    public const string RequestHeader = "X-Ne2-Factory";

    public static void AddControlPanel(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    public static void UseControlPanel(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !context.Request.Headers.ContainsKey(RequestHeader))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            try
            {
                await next(context);
            }
            catch (Exception ex) when (!context.Response.HasStarted)
            {
                await Error(ex.Message, StatusCodes.Status500InternalServerError).ExecuteAsync(context);
            }
        });

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            // Plain files, no build step: always revalidate so a new version shows up on reload.
            OnPrepareResponse = file => file.Context.Response.Headers.CacheControl = "no-cache",
        });

        var api = app.MapGroup("/api");
        api.MapProjectEndpoints();
        api.MapWorkerEndpoints();
        api.MapBacklogEndpoints();
        api.MapRunsEndpoints();
        api.MapGapScoutEndpoints();
    }

    public static IResult Error(string message, int statusCode) =>
        Results.Json(new { error = message }, statusCode: statusCode);
}
