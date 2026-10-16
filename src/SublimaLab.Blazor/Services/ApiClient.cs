using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SublimaLab.Blazor.Models;

namespace SublimaLab.Blazor.Services;

/// <summary>A failed API call, carrying the server's structured error.</summary>
public sealed class ApiException(HttpStatusCode status, ApiError error) : Exception(error.Message)
{
    public HttpStatusCode Status { get; } = status;
    public ApiError Error { get; } = error;
    public string Code => Error.Code;

    /// <summary>Field errors keyed by camelCase JSON path (e.g. "contact.email").</summary>
    public IReadOnlyDictionary<string, string[]> FieldErrors => Error.Errors ?? [];
}

/// <summary>
/// Typed HTTP client for the SublimaLab API. The browser sends the HttpOnly auth cookie
/// automatically (same origin); this client adds the antiforgery header to unsafe requests
/// and transparently refreshes the token once if the server rejects it. GET responses are
/// shared with <see cref="PrerenderCache"/> so prerendered pages don't fetch everything twice.
/// </summary>
public sealed class ApiClient(HttpClient http, PrerenderCache? prerender = null)
{
    public static JsonSerializerOptions Json => ApiJsonContext.Default.Options;

    private const string AntiforgeryHeader = "X-XSRF-TOKEN";
    private string? _antiforgeryToken;

    public Task<T> GetAsync<T>(string url, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, url, null, cancellationToken);

    public Task<T> PostAsync<T>(string url, object? body = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, url, body, cancellationToken);

    public Task<T> PutAsync<T>(string url, object? body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Put, url, body, cancellationToken);

    public Task<T> DeleteAsync<T>(string url, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Delete, url, null, cancellationToken);

    public async Task PostAsync(string url, object? body = null, CancellationToken cancellationToken = default) =>
        await SendRawAsync(HttpMethod.Post, url, body, cancellationToken);

    public async Task DeleteAsync(string url, CancellationToken cancellationToken = default) =>
        await SendRawAsync(HttpMethod.Delete, url, null, cancellationToken);

    /// <summary>Stores the token returned after sign-in/sign-out (it is bound to the user).</summary>
    public void UseAntiforgeryToken(string token) => _antiforgeryToken = token;

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        var typeInfo = (JsonTypeInfo<T>)Json.GetTypeInfo(typeof(T));
        var isGet = method == HttpMethod.Get;
        if (isGet && prerender is not null && prerender.TryTake(url, out var handedOff))
        {
            return JsonSerializer.Deserialize(handedOff, typeInfo)!;
        }

        using var response = await SendRawAsync(method, url, body, cancellationToken);
        if (isGet && prerender is { IsRecording: true })
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            prerender.Record(url, json);
            return JsonSerializer.Deserialize(json, typeInfo)!;
        }

        return (await response.Content.ReadFromJsonAsync(typeInfo, cancellationToken))!;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken, bool isRetry = false)
    {
        var unsafeMethod = method != HttpMethod.Get && method != HttpMethod.Head;
        if (unsafeMethod && _antiforgeryToken is null)
        {
            await RefreshAntiforgeryTokenAsync(cancellationToken);
        }

        using var request = new HttpRequestMessage(method, url);
        if (body is HttpContent content)
        {
            request.Content = content;
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        if (unsafeMethod)
        {
            request.Headers.Add(AntiforgeryHeader, _antiforgeryToken);
        }

        var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var error = await ReadErrorAsync(response, cancellationToken);
        response.Dispose();

        if (error.Code == "CSRF_TOKEN_INVALID" && !isRetry)
        {
            await RefreshAntiforgeryTokenAsync(cancellationToken);
            return await SendRawAsync(method, url, body, cancellationToken, isRetry: true);
        }

        throw new ApiException(response.StatusCode, error);
    }

    private async Task RefreshAntiforgeryTokenAsync(CancellationToken cancellationToken)
    {
        var token = await http.GetFromJsonAsync<AntiforgeryToken>("api/antiforgery/token", Json, cancellationToken);
        _antiforgeryToken = token!.Token;
    }

    private static async Task<ApiError> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(Json, cancellationToken);
            if (error is not null && !string.IsNullOrEmpty(error.Code))
            {
                return error;
            }
        }
        catch (JsonException)
        {
            // Fall through to a generic error (e.g. a proxy returned HTML).
        }
        catch (NotSupportedException)
        {
            // Non-JSON content type.
        }

        return new ApiError("HTTP_ERROR", response.StatusCode == HttpStatusCode.TooManyRequests
            ? "Too many requests. Please wait a moment and try again."
            : "We couldn't reach SublimaLab right now. Please try again.", null, null);
    }
}
