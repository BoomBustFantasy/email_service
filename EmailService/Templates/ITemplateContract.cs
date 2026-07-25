namespace EmailService.Templates;

/// <summary>
/// Base interface for all email template contracts
/// </summary>
public interface ITemplateContract
{
    /// <summary>
    /// Internal template key used to identify the template
    /// </summary>
    string TemplateKey { get; }
    
    /// <summary>
    /// Validates that all required template variables are present and valid
    /// </summary>
    /// <param name="errors">Collection of validation errors, if any</param>
    /// <returns>True if validation passes, false otherwise</returns>
    bool Validate(out List<string> errors);
    
    /// <summary>
    /// Converts the template data into a dictionary for Brevo API
    /// </summary>
    Dictionary<string, string> ToBrevoParams();
    
    /// <summary>
    /// Optional sender override for this specific email
    /// </summary>
    SenderIdentity? SenderOverride { get; }
}
