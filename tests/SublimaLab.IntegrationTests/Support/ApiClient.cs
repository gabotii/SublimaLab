using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SublimaLab.IntegrationTests.Support;

/// <summary>
/// Thin HTTP client that behaves like the real frontend: keeps cookies, sends the antiforgery
/// header on unsafe requests and refreshes the token after sign-in / sign-out.
/// </summary>
public sealed class ApiClient(HttpClient http) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        AllowOutOfOrderMetadataProperties = true,
    };

    private string? _antiforgeryToken;

    public HttpClient Http => http;

    public async Task RefreshAntiforgeryAsync()
    {
        var token = await GetAsync<JsonElement>("/api/antiforgery/token");
        _antiforgeryToken = token.GetProperty("token").GetString();
    }

    public void UseAntiforgeryToken(string token) => _antiforgeryToken = token;

    public async Task<T> GetAsync<T>(string url)
    {
        var response = await http.GetAsync(url);
        return await ReadAsync<T>(response, HttpStatusCode.OK);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, bool withAntiforgery = true)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is HttpContent content)
        {
            request.Content = content;
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        if (withAntiforgery && _antiforgeryToken is not null)
        {
            request.Headers.Add("X-XSRF-TOKEN", _antiforgeryToken);
        }

        return http.SendAsync(request);
    }

    public async Task<T> PostAsync<T>(string url, object? body, HttpStatusCode expected = HttpStatusCode.OK) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Post, url, body), expected);

    public async Task<T> PutAsync<T>(string url, object? body, HttpStatusCode expected = HttpStatusCode.OK) =>
        await ReadAsync<T>(await SendAsync(HttpMethod.Put, url, body), expected);

    public async Task<JsonElement> LoginAsync(string email, string password)
    {
        var result = await PostAsync<JsonElement>("/api/auth/login", new { email, password, rememberMe = false });
        _antiforgeryToken = result.GetProperty("antiforgery").GetProperty("token").GetString();
        return result.GetProperty("user");
    }

    public async Task<JsonElement> UploadAsync(byte[] bytes, string fileName, string contentType, string kind = "Artwork", HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(kind), "kind");
        return await ReadAsync<JsonElement>(await SendAsync(HttpMethod.Post, "/api/uploads", form), expected);
    }

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != expected)
        {
            throw new HttpRequestException($"Expected {(int)expected} but got {(int)response.StatusCode} for {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");
        }

        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public void Dispose() => http.Dispose();
}
