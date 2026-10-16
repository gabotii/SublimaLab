namespace SublimaLab.Api.Http;

/// <summary>Baseline security headers for every response.</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    // Storefront policy: WebAssembly needs 'wasm-unsafe-eval'; brand fonts come from Google Fonts.
    // Inline styles are allowed for component style attributes (e.g. colour swatches); scripts are not.
    private const string StorefrontPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: blob:; " +
        "connect-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    // JSON APIs never render HTML: lock the document down completely.
    private const string ApiPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            // Responses that set their own policy (e.g. sandboxed upload content) keep it.
            if (!headers.ContainsKey("Content-Security-Policy"))
            {
                var path = context.Request.Path;
                if (path.StartsWithSegments("/api"))
                {
                    headers.ContentSecurityPolicy = ApiPolicy;
                }
                else if (!path.StartsWithSegments("/swagger") && !path.StartsWithSegments("/_framework/debug"))
                {
                    headers.ContentSecurityPolicy = StorefrontPolicy;
                }
            }

            return Task.CompletedTask;
        });

        return next(context);
    }
}
