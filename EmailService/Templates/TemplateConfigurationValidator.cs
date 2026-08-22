namespace EmailService.Templates;

/// <summary>
/// Fails the service at startup when the template wiring is incomplete.
///
/// Both halves of a template have to line up: a contract class registered in
/// <see cref="ITemplateContractRegistry"/>, and a Brevo numeric ID in
/// TemplateIdMap. When one is missing, the failure used to surface per-message
/// at runtime and was easy to miss — an unmapped key threw KeyNotFoundException
/// inside the send and retried forever (one message was retried 217 times in
/// July 2026), and a mapped key with no contract was archived as "invalid",
/// silently dropping mail the producer had legitimately queued.
///
/// A misconfiguration is a deploy problem, so it belongs at boot where it is
/// loud, not per-message where it is invisible.
/// </summary>
public static class TemplateConfigurationValidator
{
    public static void Validate(ITemplateContractRegistry registry, TemplateConfig config)
    {
        var problems = new List<string>();
        var map = config.TemplateIdMap;

        foreach (var key in registry.RegisteredKeys.Order())
        {
            if (!map.TryGetValue(key, out var templateId))
            {
                problems.Add(
                    $"'{key}' has a contract but no entry in TemplateIdMap — messages using it would retry forever.");
            }
            else if (templateId <= 0)
            {
                problems.Add(
                    $"'{key}' is mapped to Brevo template ID {templateId}, which is not a real template. " +
                    "Create it in Brevo and set the ID (see docs/email-templates.md).");
            }
        }

        foreach (var key in map.Keys.Order())
        {
            if (!registry.RegisteredKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"'{key}' has a Brevo template ID but no registered contract — messages using it " +
                    "would be dropped as invalid.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "Template configuration is incomplete:" + Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
        }
    }
}
