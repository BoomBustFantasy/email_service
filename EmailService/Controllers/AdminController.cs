using Microsoft.AspNetCore.Mvc;
using EmailService.Filters;
using EmailService.SupabaseModels;
using Supabase;

namespace EmailService.Controllers;

/// <summary>
/// Admin API for operational controls
/// </summary>
[ApiController]
[Route("admin")]
public class AdminController : ControllerBase
{
    private readonly ILogger<AdminController> _logger;
    private readonly Client _supabaseClient;
    
    public AdminController(
        ILogger<AdminController> logger,
        Client supabaseClient)
    {
        _logger = logger;
        _supabaseClient = supabaseClient;
    }
    
    /// <summary>
    /// Replay dead letter messages back to the queue
    /// </summary>
    /// <param name="messageIds">List of dead letter message IDs to replay (max 100)</param>
    [HttpPost("replay-dead-letters")]
    [AdminAuth]
    public async Task<IActionResult> ReplayDeadLetters([FromBody] List<long> messageIds)
    {
        try
        {
            // Validate batch size
            if (messageIds.Count == 0)
            {
                return BadRequest(new { error = "No message IDs provided" });
            }
            
            if (messageIds.Count > 100)
            {
                return BadRequest(new { error = "Maximum batch size is 100 messages" });
            }
            
            _logger.LogInformation("Replaying {Count} dead letter messages", messageIds.Count);
            
            var results = new List<ReplayResult>();
            
            foreach (var messageId in messageIds)
            {
                try
                {
                    var result = await ReplayDeadLetterMessageAsync(messageId);
                    results.Add(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error replaying dead letter message {MessageId}", messageId);
                    results.Add(new ReplayResult
                    {
                        DeadLetterMessageId = messageId,
                        Success = false,
                        Error = ex.Message
                    });
                }
            }
            
            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);
            
            _logger.LogInformation(
                "Replay complete: {SuccessCount} succeeded, {FailCount} failed",
                successCount,
                failCount);
            
            return Ok(new
            {
                total = results.Count,
                succeeded = successCount,
                failed = failCount,
                results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replaying dead letter messages");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
    
    private async Task<ReplayResult> ReplayDeadLetterMessageAsync(long deadLetterMessageId)
    {
        // Fetch the dead letter message
        var dlqResponse = await _supabaseClient
            .From<DeadLetterEmail>()
            .Where(x => x.Id == deadLetterMessageId)
            .Get();
        
        if (dlqResponse.Models.Count == 0)
        {
            _logger.LogWarning("Dead letter message {MessageId} not found", deadLetterMessageId);
            return new ReplayResult
            {
                DeadLetterMessageId = deadLetterMessageId,
                Success = false,
                Error = "Message not found"
            };
        }
        
        var dlqMessage = dlqResponse.Models.First();
        
        // Check if original queue message still exists
        OutboundEmailQueue? originalMessage = null;
        if (dlqMessage.OriginalQueueId != null)
        {
            var originalResponse = await _supabaseClient
                .From<OutboundEmailQueue>()
                .Where(x => x.Id == dlqMessage.OriginalQueueId)
                .Get();
            
            if (originalResponse.Models.Count > 0)
            {
                originalMessage = originalResponse.Models.First();
            }
        }
        
        // If original exists and is already dead_lettered, reset it
        if (originalMessage != null && originalMessage.Status == "dead_lettered")
        {
            await _supabaseClient
                .From<OutboundEmailQueue>()
                .Where(x => x.Id == originalMessage.Id)
                .Set(x => x.Status!, "pending")
                .Set(x => x.RetryCount!, 0)
                .Set(x => x.LockedUntil!, null)
                .Set(x => x.LastError!, null)
                .Update();
            
            _logger.LogInformation(
                "Reset original queue message {MessageId} to pending",
                originalMessage.Id);
            
            return new ReplayResult
            {
                DeadLetterMessageId = deadLetterMessageId,
                QueueMessageId = originalMessage.Id,
                Success = true
            };
        }
        
        // Otherwise, create a new queue message preserving the original idempotency key
        // Generate a new idempotency key with replay suffix to avoid conflicts
        var replayIdempotencyKey = $"{Guid.NewGuid()}-replay-{deadLetterMessageId}";
        
        var newQueueMessage = new OutboundEmailQueue
        {
            IdempotencyKey = replayIdempotencyKey,
            TemplateKey = dlqMessage.TemplateKey,
            RecipientEmail = dlqMessage.RecipientEmail,
            TemplateVariables = dlqMessage.TemplateParams,
            Status = "pending",
            RetryCount = 0
        };
        
        var insertResponse = await _supabaseClient
            .From<OutboundEmailQueue>()
            .Insert(newQueueMessage);
        
        var insertedMessage = insertResponse.Models.First();
        
        _logger.LogInformation(
            "Created new queue message {MessageId} from dead letter {DlqMessageId}",
            insertedMessage.Id,
            deadLetterMessageId);
        
        return new ReplayResult
        {
            DeadLetterMessageId = deadLetterMessageId,
            QueueMessageId = insertedMessage.Id,
            Success = true
        };
    }
}

/// <summary>
/// Result of a dead letter replay operation
/// </summary>
public class ReplayResult
{
    public long DeadLetterMessageId { get; set; }
    public long? QueueMessageId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
