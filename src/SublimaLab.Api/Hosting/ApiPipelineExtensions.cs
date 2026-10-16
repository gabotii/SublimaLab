using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.StaticFiles;
using SublimaLab.Api.Http;

namespace SublimaLab.Api.Hosting;

internal static class ApiPipelineExtensions
{
    public static WebApplication UseApi(this WebApplication app)
    {
        // Behind a TLS-terminating proxy (Docker, cloud ingress) the original scheme and client IP
        // come from X-Forwarded-* headers, accepted only from the configured proxy addresses.
        if (ReverseProxySetup.CreateOptions(app.Configuration) is { } forwarded)
        {
            app.UseForwardedHeaders(forwarded);
        }

        app.UseExceptionHandler();

        // Unknown storefront pages are handled by the not-found page's catch-all route (HTML, 404);
        // other body-less errors get the standard JSON error body.
        app.UseStatusCodePages(async context =>
        {
            // Body-less error responses (unknown route, wrong method...) get the standard error body too.
            var response = context.HttpContext.Response;
            if (response.ContentLength is null && string.IsNullOrEmpty(response.ContentType))
            {
                var (code, message) = response.StatusCode switch
                {
                    404 => ("NOT_FOUND", "The requested resource was not found."),
                    405 => ("METHOD_NOT_ALLOWED", "This HTTP method is not supported for this resource."),
                    415 => ("UNSUPPORTED_MEDIA_TYPE", "The request content type is not supported."),
                    _ => ("HTTP_ERROR", "The request could not be processed."),
                };
                await ApiErrors.WriteAsync(context.HttpContext, response.StatusCode, code, message);
            }
        });

        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "SublimaLab API v1");
                options.DocumentTitle = "SublimaLab API";
            });
            app.UseWebAssemblyDebugging();
        }

        // The storefront is served from the same origin as the API, so the auth cookie,
        // antiforgery token and SameSite rules work without any cross-origin setup.
        app.UseResponseCompression();
        app.UseBlazorFrameworkFiles(); // serves the precompressed (.br/.gz) WebAssembly runtime files
        app.UseStaticFiles(StorefrontStaticFiles());

        app.UseRouting();
        app.UseCors(ApiServiceCollectionExtensions.CorsPolicy);
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseOutputCache();

        app.MapControllers();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        // Unknown API routes keep returning JSON errors.
        app.Map("/api/{**path}", context =>
            ApiErrors.WriteAsync(context, StatusCodes.Status404NotFound, "NOT_FOUND", "The requested resource was not found."));

        // Page [Authorize] attributes are enforced by the storefront router (sign-in redirect, "access
        // denied"); private pages only render in the browser and every API call is authorized itself.
        app.MapRazorComponents<Components.App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(Blazor.Routes).Assembly)
            .AllowAnonymous();

        return app;
    }

    /// <summary>
    /// Static file settings for the storefront: glTF content types for the 3D models, and caching
    /// tuned per file kind. Fingerprinted bundles never change, entry points always revalidate.
    /// </summary>
    private static StaticFileOptions StorefrontStaticFiles()
    {
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".glb"] = "model/gltf-binary";
        contentTypes.Mappings[".gltf"] = "model/gltf+json";

        return new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                var path = context.Context.Request.Path.Value ?? string.Empty;
                var headers = context.Context.Response.Headers;
                if (path.StartsWith("/customizer/chunks/", StringComparison.Ordinal) || path.StartsWith("/customizer/assets/", StringComparison.Ordinal))
                {
                    headers.CacheControl = "public, max-age=31536000, immutable";
                }
                else if (path.StartsWith("/models/", StringComparison.Ordinal) || path.StartsWith("/images/", StringComparison.Ordinal) || path.EndsWith(".svg", StringComparison.Ordinal))
                {
                    headers.CacheControl = "public, max-age=86400";
                }
                else if (!path.StartsWith("/_framework/", StringComparison.Ordinal))
                {
                    headers.CacheControl = "no-cache";
                }
            },
        };
    }
}
