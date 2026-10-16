using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;

namespace SublimaLab.Application.Common;

public static class ValidationExtensions
{
    public static async Task ValidateOrThrowAsync<T>(this IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (!result.IsValid)
        {
            throw new RequestValidationException(result.ToCamelCaseDictionary());
        }
    }

    /// <summary>Converts "Contact.Email" / "Surfaces[0].Elements[1].Text" to camelCase JSON paths.</summary>
    public static IReadOnlyDictionary<string, string[]> ToCamelCaseDictionary(this ValidationResult result, string? prefix = null) =>
        result.Errors
            .GroupBy(e => ToJsonPath(e.PropertyName, prefix))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

    public static string ToJsonPath(string propertyName, string? prefix = null)
    {
        var path = string.Join('.', propertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
        return string.IsNullOrEmpty(prefix) ? path : string.IsNullOrEmpty(path) ? prefix : $"{prefix}.{path}";
    }
}
