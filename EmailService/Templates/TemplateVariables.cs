using System.Net.Mail;

namespace EmailService.Templates;

/// <summary>
/// Shared validation and lookup helpers for template contracts. Producers send
/// template variables as a flat string dictionary; contracts read the values they
/// require through these helpers so every contract reports errors the same way.
/// </summary>
public static class TemplateVariables
{
    /// <summary>
    /// Reads a variable, returning an empty string when it is absent. Contracts
    /// report the absence through their own Validate() so every missing field is
    /// collected in one pass instead of throwing on the first one.
    /// </summary>
    public static string Read(IReadOnlyDictionary<string, string> variables, string key) =>
        variables.TryGetValue(key, out var value) ? value : string.Empty;

    public static void RequireText(string? value, string variableName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{variableName} is required");
        }
    }

    public static void RequireEmail(string? value, string variableName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{variableName} is required");
            return;
        }

        if (!IsValidEmail(value))
        {
            errors.Add($"{variableName} '{value}' is not a valid email address");
        }
    }

    public static void RequireUrl(string? value, string variableName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{variableName} is required");
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{variableName} '{value}' is not a valid absolute http(s) URL");
        }
    }

    public static bool IsValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return address.Address == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
