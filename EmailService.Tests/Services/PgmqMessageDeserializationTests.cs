using EmailService.Services;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;

namespace EmailService.Tests.Services;

/// <summary>
/// read_email_queue returns snake_case columns. When PgmqMessage had no
/// [JsonProperty] attributes, Newtonsoft left MsgId and ReadCount at their
/// defaults and reported no error — so every archive call targeted message 0
/// and removed nothing, and read_ct never reached the retry limit. Nothing was
/// ever archived off the queue in the service's lifetime because of this.
/// </summary>
public class PgmqMessageDeserializationTests
{
    /// <summary>Verbatim shape of a read_email_queue row.</summary>
    private const string RpcRow = """
    {
        "msg_id": 241,
        "read_ct": 3,
        "enqueued_at": "2026-08-22T16:43:15.506836+00:00",
        "vt": "2026-08-23T17:59:05.185+00:00",
        "message": { "outbox_id": "2a8d71dc-6c1a-4653-8c33-b93289505ee5" }
    }
    """;

    [Fact]
    public void MsgId_IsDeserialized()
    {
        var message = JsonConvert.DeserializeObject<PgmqMessage>(RpcRow)!;

        message.MsgId.Should().Be(241, because: "archiving message 0 removes nothing from the queue");
    }

    [Fact]
    public void ReadCount_IsDeserialized()
    {
        var message = JsonConvert.DeserializeObject<PgmqMessage>(RpcRow)!;

        message.ReadCount.Should().Be(3, because: "a read count stuck at 0 can never exhaust the retry budget");
    }

    [Fact]
    public void EnqueuedAt_IsDeserialized()
    {
        var message = JsonConvert.DeserializeObject<PgmqMessage>(RpcRow)!;

        message.EnqueuedAt.ToUniversalTime().Should().BeCloseTo(
            new DateTime(2026, 8, 22, 16, 43, 15, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MessagePayload_IsDeserialized()
    {
        var message = JsonConvert.DeserializeObject<PgmqMessage>(RpcRow)!;

        message.Message.Should().NotBeNull();
        JsonConvert.SerializeObject(message.Message).Should().Contain("2a8d71dc");
    }

    /// <summary>
    /// The two failures compound: a message that is read but never archived comes
    /// back forever, and a read count of 0 means it is never dead-lettered either.
    /// </summary>
    [Fact]
    public void DeserializedReadCount_FeedsTheRetryBudget()
    {
        var message = JsonConvert.DeserializeObject<PgmqMessage>(RpcRow)!;

        QueueConsumerService.HasExhaustedRetries(message.ReadCount, maxRetryAttempts: 3)
            .Should().BeTrue();
    }
}
