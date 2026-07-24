using System.Text.Json;
using First10.Application.Outbound;
using First10.Domain.Abstractions;
using First10.Domain.Channels;
using First10.Domain.Conversations;
using First10.Domain.Incidents;
using First10.Infrastructure.Persistence;
using First10.Infrastructure.Telegram;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace First10.Tests;

/// <summary>
/// Telegram adapter: update JSON maps to the normalized envelope exactly (payload
/// shapes from the Bot API docs), unknown types are never dropped (D-008), and the
/// outbound handler routes per-channel — Local stays timeline-only, Telegram goes
/// through the registered sender, and a missing sender still records the timeline.
/// </summary>
public class TelegramAdapterTests
{
    private static JsonElement Update(string json) => JsonDocument.Parse(json).RootElement;

    // ---- Mapper ----

    [Fact]
    public void Text_message_maps_to_text_envelope()
    {
        var mapped = TelegramUpdateMapper.Map(Update("""
            {"update_id":1,"message":{"message_id":42,"date":1753350000,
             "chat":{"id":987654321,"type":"private"},
             "from":{"id":987654321,"first_name":"Ade"},
             "text":"accident dey happen for kara bridge o"}}
            """))!;

        Assert.Equal(InboundKind.Text, mapped.Kind);
        Assert.Equal(987654321, mapped.ChatId);
        Assert.Equal("987654321:42", mapped.ExternalMessageId); // chat-qualified (message_id is per-chat)
        Assert.Equal("accident dey happen for kara bridge o", mapped.Text);
        Assert.Null(mapped.FileId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1753350000), mapped.OccurredAt);
    }

    [Fact]
    public void Photo_message_picks_the_largest_resolution_and_keeps_the_caption()
    {
        var mapped = TelegramUpdateMapper.Map(Update("""
            {"update_id":2,"message":{"message_id":43,"date":1753350001,
             "chat":{"id":11,"type":"private"},
             "caption":"see the crash",
             "photo":[
               {"file_id":"small","width":90,"height":60},
               {"file_id":"medium","width":320,"height":240},
               {"file_id":"large","width":1280,"height":960}]}}
            """))!;

        Assert.Equal(InboundKind.Image, mapped.Kind);
        Assert.Equal("large", mapped.FileId);
        Assert.Equal("image/jpeg", mapped.MediaContentType);
        Assert.Equal("see the crash", mapped.Text);
    }

    [Fact]
    public void Voice_note_maps_to_voice_with_ogg_content_type()
    {
        var mapped = TelegramUpdateMapper.Map(Update("""
            {"update_id":3,"message":{"message_id":44,"date":1753350002,
             "chat":{"id":11,"type":"private"},
             "voice":{"file_id":"voice-abc","duration":7,"mime_type":"audio/ogg"}}}
            """))!;

        Assert.Equal(InboundKind.Voice, mapped.Kind);
        Assert.Equal("voice-abc", mapped.FileId);
        Assert.Equal("audio/ogg", mapped.MediaContentType);
    }

    [Fact]
    public void Video_maps_to_image_kind_for_the_contact_sheet_path()
    {
        var mapped = TelegramUpdateMapper.Map(Update("""
            {"update_id":4,"message":{"message_id":45,"date":1753350003,
             "chat":{"id":11,"type":"private"},
             "video":{"file_id":"vid-1","duration":6,"mime_type":"video/mp4"}}}
            """))!;

        Assert.Equal(InboundKind.Image, mapped.Kind); // D-019: stored artifact is the blurred sheet
        Assert.Equal("vid-1", mapped.FileId);
        Assert.Equal("video/mp4", mapped.MediaContentType);
    }

    [Fact]
    public void Location_maps_to_pin()
    {
        var mapped = TelegramUpdateMapper.Map(Update("""
            {"update_id":5,"message":{"message_id":46,"date":1753350004,
             "chat":{"id":11,"type":"private"},
             "location":{"latitude":6.665,"longitude":3.383}}}
            """))!;

        Assert.Equal(InboundKind.LocationPin, mapped.Kind);
        Assert.Equal(6.665, mapped.Location!.Value.Latitude, 3);
        Assert.Equal(3.383, mapped.Location!.Value.Longitude, 3);
    }

    [Fact]
    public void Sticker_is_never_silently_dropped()
    {
        var mapped = TelegramUpdateMapper.Map(Update("""
            {"update_id":6,"message":{"message_id":47,"date":1753350005,
             "chat":{"id":11,"type":"private"},
             "sticker":{"file_id":"stk","width":512,"height":512}}}
            """))!;

        Assert.Equal(InboundKind.Text, mapped.Kind); // flows through triage → guided reply, not silence (D-008)
        Assert.Equal("[unsupported message type]", mapped.Text);
    }

    [Fact]
    public void Non_message_updates_are_ignored()
    {
        Assert.Null(TelegramUpdateMapper.Map(Update("""{"update_id":7,"edited_message":{"message_id":1}}""")));
    }

    // ---- Outbound routing ----

    private sealed class RecordingSender : IOutboundChannelSender
    {
        public List<(string To, string Text)> Sent { get; } = [];
        public ChannelKind Channel => ChannelKind.Telegram;
        public Task SendAsync(string externalUserId, string text, CancellationToken ct)
        {
            Sent.Add((externalUserId, text));
            return Task.CompletedTask;
        }
    }

    private static First10DbContext NewDb() =>
        new(new DbContextOptionsBuilder<First10DbContext>()
            .UseInMemoryDatabase($"first10-{Guid.NewGuid()}")
            .Options);

    private static async Task<Conversation> Conversation(First10DbContext db, ChannelKind channel)
    {
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            Channel = channel,
            ExternalUserId = channel == ChannelKind.Telegram ? "987654321" : "persona-1",
            CreatedAt = DateTimeOffset.UtcNow,
            LastInboundAt = DateTimeOffset.UtcNow,
        };
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        return conversation;
    }

    [Fact]
    public async Task Telegram_outbound_goes_through_the_sender_and_records_the_timeline()
    {
        await using var db = NewDb();
        var conversation = await Conversation(db, ChannelKind.Telegram);
        var sender = new RecordingSender();

        await OutboundMessageHandler.Handle(
            new SendOutboundMessage(conversation.Id, null, OutboundKind.PinReceivedAck, "pidgin"),
            db, [sender], NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        var (to, text) = Assert.Single(sender.Sent);
        Assert.Equal("987654321", to);
        Assert.False(string.IsNullOrWhiteSpace(text));
        var entry = db.TimelineEntries.Single();
        Assert.Equal(TimelineDirection.Outbound, entry.Direction);
        Assert.Equal(text, entry.Text); // console evidence matches what the reporter received
    }

    [Fact]
    public async Task Local_outbound_never_touches_a_channel_sender()
    {
        await using var db = NewDb();
        var conversation = await Conversation(db, ChannelKind.Local);
        var sender = new RecordingSender();

        await OutboundMessageHandler.Handle(
            new SendOutboundMessage(conversation.Id, null, OutboundKind.PinReceivedAck, "english"),
            db, [sender], NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Empty(sender.Sent);
        Assert.Single(db.TimelineEntries); // timeline entry IS the cockpit delivery
    }

    [Fact]
    public async Task Missing_sender_still_records_the_timeline_entry()
    {
        await using var db = NewDb();
        var conversation = await Conversation(db, ChannelKind.Telegram);

        await OutboundMessageHandler.Handle(
            new SendOutboundMessage(conversation.Id, null, OutboundKind.PinReceivedAck, "english"),
            db, [], NullLogger.Instance, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Single(db.TimelineEntries); // what we FAILED to deliver stays visible
    }
}
