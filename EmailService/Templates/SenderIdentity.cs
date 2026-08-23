namespace EmailService.Templates;

/// <summary>
/// Represents sender identity information for an email
/// </summary>
public class SenderIdentity
{
    public required string Email { get; init; }
    public required string Name { get; init; }
}
