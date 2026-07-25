using Microsoft.Extensions.Options;

namespace EmailService.Templates;

/// <summary>
/// Service for resolving template contracts to Brevo template IDs and managing sender identities
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// Resolves an internal template key to a Brevo numeric template ID
    /// </summary>
    /// <param name="templateKey">Internal template key</param>
    /// <returns>Brevo template ID</returns>
    /// <exception cref="KeyNotFoundException">Thrown when template key is not configured</exception>
    long ResolveTemplateId(string templateKey);
    
    /// <summary>
    /// Gets the sender identity for a template, applying overrides if provided
    /// </summary>
    /// <param name="contract">Template contract that may contain a sender override</param>
    /// <returns>Sender identity to use</returns>
    SenderIdentity GetSenderIdentity(ITemplateContract contract);
    
    /// <summary>
    /// Validates a template contract
    /// </summary>
    /// <param name="contract">Template contract to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    void ValidateContract(ITemplateContract contract);
}

public class TemplateService : ITemplateService
{
    private readonly TemplateConfig _config;
    
    public TemplateService(IOptions<TemplateConfig> config)
    {
        _config = config.Value;
    }
    
    public long ResolveTemplateId(string templateKey)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("Template key cannot be null or empty", nameof(templateKey));
        
        if (!_config.TemplateIdMap.TryGetValue(templateKey, out var templateId))
        {
            throw new KeyNotFoundException($"Template key '{templateKey}' is not configured in TemplateIdMap");
        }
        
        return templateId;
    }
    
    public SenderIdentity GetSenderIdentity(ITemplateContract contract)
    {
        return contract.SenderOverride ?? _config.DefaultSender;
    }
    
    public void ValidateContract(ITemplateContract contract)
    {
        if (!contract.Validate(out var errors))
        {
            var errorMessage = string.Join("; ", errors);
            throw new ArgumentException($"Template contract validation failed: {errorMessage}");
        }
    }
}
