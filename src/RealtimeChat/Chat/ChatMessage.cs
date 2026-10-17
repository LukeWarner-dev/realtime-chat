namespace RealtimeChat.Chat;

public record ChatMessage(
    string Id,
    string Room,
    string User,
    string Text,
    DateTimeOffset SentAt,
    bool IsSystem = false)
{
    public static ChatMessage FromUser(string room, string user, string text) =>
        new(Guid.NewGuid().ToString("N"), room, user, text, DateTimeOffset.UtcNow);

    public static ChatMessage System(string room, string text) =>
        new(Guid.NewGuid().ToString("N"), room, "", text, DateTimeOffset.UtcNow, IsSystem: true);
}

/// <summary>What the caller gets back after joining a room.</summary>
public record JoinResult(string Room, IReadOnlyList<ChatMessage> History, IReadOnlyList<string> Users);
