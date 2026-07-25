using Microsoft.AspNetCore.Mvc;
using EmailService.Filters;
using EmailService.SupabaseModels;
using Supabase;
using System.Text.Json;

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
    private const string QueueName = "email_outbound";
    private const string DlqName = "email_outbound_dlq";
    
    public AdminController(
        ILogger<AdminController> logger,
        Client supabaseClient)
    {
        _logger = logger;
        _supabaseClient = supabaseClient;
    }
    
    /// <summary>
    /// Replay dead letter messages back to the main queue
    /// </summary>
    /// <param name="messageIds">List of pgmq DLQ message IDs to replay (max 100)</param>
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
            
            _logger.LogInformation("Replaying {Count} dead letter messages from pgmq", messageIds.Count);
            
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
    
    /// <summary>
    /// Get dead letter queue statistics
    /// </summary>
    [HttpGet("dead-letter-stats")]
    [AdminAuth]
    public async Task<IActionResult> GetDeadLetterStats()
    {
        try
        {
            // Get DLQ depth
            var dlqMetrics = await _supabaseClient.Rpc<PgmqMetrics>(
                "pgmq.metrics",
                new Dictionary<string, object>
                {
                    { "queue_name", DlqName }
                });
            
            return Ok(new
            {
                dlq_depth = dlqMetrics?.QueueLength ?? 0,
                queue_name = DlqName
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting DLQ stats");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
    
    private async Task<ReplayResult> ReplayDeadLetterMessageAsync(long dlqMessageId)
    {
        // Read the message from DLQ (this makes it visible to us)
        var dlqMessages = await _supabaseClient.Rpc<PgmqDlqMessage[]>(
            "pgmq.read",
            new Dictionary<string, object>
            {
                { "queue_name", DlqName },
                { "vt", 300 }, // 5 minute visibility timeout
                { "qty", 100 }
            });

        if (dlqMessages == null || dlqMessages.Length == 0)
        {
            return new ReplayResult
            {
                DeadLetterMessageId = dlqMessageId,
                Success = false,
                Error = "Message not found in DLQ"
            };
        }

        var dlqMessage = dlqMessages.FirstOrDefault(m => m.MsgId == dlqMessageId);
        if (dlqMessage == null)
        {
            return new ReplayResult
            {
                DeadLetterMessageId = dlqMessageId,
                Success = false,
                Error = "Message not found in DLQ"
            };
        }

        try
        {
            // Re-enqueue the message to the main queue
            var sendResult = await _supabaseClient.Rpc<long?>(
                "pgmq.send",
                new Dictionary<string, object>
                {
                    { "queue_name", QueueName },
                    { "msg", dlqMessage.Message }
                });

            if (sendResult.HasValue)
            {
                // Delete from DLQ
                await _supabaseClient.Rpc(
                    "pgmq.delete",
                    new Dictionary<string, object>
                    {
                        { "queue_name", DlqName },
                        { "msg_id", dlqMessageId }
                    });

                _logger.LogInformation(
                    "Replayed message {DlqMessageId} from DLQ to main queue as message {NewMessageId}",
                    dlqMessageId,
                    sendResult.Value);

                return new ReplayResult
                {
                    DeadLetterMessageId = dlqMessageId,
                    NewQueueMessageId = sendResult.Value,
                    Success = true
                };
            }
            else
            {
                return new ReplayResult
                {
                    DeadLetterMessageId = dlqMessageId,
                    Success = false,
                    Error = "Failed to send message to main queue"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replaying message {MessageId}", dlqMessageId);
            return new ReplayResult
            {
                DeadLetterMessageId = dlqMessageId,
                Success = false,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// Result of a dead letter replay operation
/// </summary>
public class ReplayResult
{
    public long DeadLetterMessageId { get; set; }
    public long? NewQueueMessageId { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Represents a message from pgmq DLQ
/// </summary>
public class PgmqDlqMessage
{
    public long MsgId { get; set; }
    public int ReadCount { get; set; }
    public DateTime EnqueuedAt { get; set; }
    public DateTime Vt { get; set; }
    public required string Message { get; set; }
}

/// <summary>
/// Represents pgmq queue metrics
/// </summary>
public class PgmqMetrics
{
    public string QueueName { get; set; } = string.Empty;
    public long QueueLength { get; set; }
    public int? NewestMsgAgeSec { get; set; }
    public int? OldestMsgAgeSec { get; set; }
    public long TotalMessages { get; set; }
    public DateTime ScrapeTime { get; set; }
}
