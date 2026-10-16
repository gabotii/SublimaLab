using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace SublimaLab.Api.Http;

/// <summary>The single error body returned by every endpoint.</summary>
public sealed record ApiError(
    string Code,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? Errors,
    string TraceId);

internal static class ApiErrors
{
    public static ApiError Create(HttpContext context, string code, string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(code, message, errors, Activity.Current?.Id ?? context.TraceIdentifier);

    public static async Task WriteAsync(HttpContext context, int statusCode, string code, string message, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, Create(context, code, message, errors), JsonSerializerOptions.Web, context.RequestAborted);
    }

    public static ObjectResult Result(HttpContext context, int statusCode, string code, string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(Create(context, code, message, errors)) { StatusCode = statusCode, ContentTypes = { "application/json" } };

    /// <summary>Maps MVC model-binding failures (malformed JSON, wrong types) to the standard error body.</summary>
    public static IActionResult FromModelState(ActionContext context)
    {
        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => NormalizeKey(entry.Key),
                entry => entry.Value!.Errors.Select(e => string.IsNullOrEmpty(e.ErrorMessage) ? "The value is invalid." : e.ErrorMessage).ToArray());

        return Result(context.HttpContext, StatusCodes.Status400BadRequest, "VALIDATION_ERROR", "One or more fields are invalid.", errors);
    }

    private static string NormalizeKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key is "$" or "request" or "body")
        {
            return "body";
        }

        key = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        return string.Join('.', key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
    }
}
