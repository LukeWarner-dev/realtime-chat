using System.Text.RegularExpressions;

namespace RealtimeChat.Chat;

/// <summary>
/// Keeps track of who is connected, which room they're in, and the recent
/// messages in each room. Everything is in memory, so it resets when the
/// server restarts. Registered as a singleton because SignalR creates a new
/// hub instance for every call.
/// </summary>
public partial class ChatState
{
    public const int HistoryLimit = 50;
    public const int MaxNameLength = 24;
    public const int MaxRoomLength = 30;
    public const int MaxMessageLength = 500;

    public static readonly string[] DefaultRooms = ["General", "Random", "Help"];

    private readonly object _lock = new();
    private readonly Dictionary<string, Member> _members = new();
    private readonly Dictionary<string, Queue<ChatMessage>> _history = new(StringComparer.OrdinalIgnoreCase);

    public ChatState()
    {
        foreach (var room in DefaultRooms)
            _history[room] = new Queue<ChatMessage>();
    }

    public record Member(string UserName, string Room);

    public Member? GetMember(string connectionId)
    {
        lock (_lock)
            return _members.GetValueOrDefault(connectionId);
    }

    /// <summary>
    /// Adds or moves a connection into a room. Returns the room it was in
    /// before (if any) so the hub can tell that room the user left.
    /// </summary>
    public string? Join(string connectionId, string userName, string room)
    {
        lock (_lock)
        {
            var taken = _members.Any(m =>
                m.Key != connectionId &&
                string.Equals(m.Value.UserName, userName, StringComparison.OrdinalIgnoreCase));
            if (taken)
                throw new ChatException($"The name \"{userName}\" is already in use.");

            var previous = _members.GetValueOrDefault(connectionId)?.Room;

            // keep the room's original casing if it already exists
            var existing = _history.Keys.FirstOrDefault(k => string.Equals(k, room, StringComparison.OrdinalIgnoreCase));
            room = existing ?? room;
            if (existing is null)
                _history[room] = new Queue<ChatMessage>();

            _members[connectionId] = new Member(userName, room);

            if (previous is not null && !string.Equals(previous, room, StringComparison.OrdinalIgnoreCase))
                RemoveRoomIfEmpty(previous);

            return previous;
        }
    }

    public Member? Leave(string connectionId)
    {
        lock (_lock)
        {
            if (!_members.Remove(connectionId, out var member))
                return null;
            RemoveRoomIfEmpty(member.Room);
            return member;
        }
    }

    public void AddMessage(ChatMessage message)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(message.Room, out var queue))
                return;
            queue.Enqueue(message);
            while (queue.Count > HistoryLimit)
                queue.Dequeue();
        }
    }

    public IReadOnlyList<ChatMessage> GetHistory(string room)
    {
        lock (_lock)
            return _history.TryGetValue(room, out var queue) ? queue.ToList() : [];
    }

    public IReadOnlyList<string> GetUsers(string room)
    {
        lock (_lock)
        {
            return _members.Values
                .Where(m => string.Equals(m.Room, room, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.UserName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Every room, with the default rooms first, and how many people are in each.</summary>
    public IReadOnlyList<RoomInfo> GetRooms()
    {
        lock (_lock)
        {
            return _history.Keys
                .OrderBy(r => Array.FindIndex(DefaultRooms, d => d == r) is var i && i >= 0 ? i : int.MaxValue)
                .ThenBy(r => r, StringComparer.OrdinalIgnoreCase)
                .Select(r => new RoomInfo(r, _members.Values.Count(m => m.Room == r)))
                .ToList();
        }
    }

    // custom rooms disappear (with their history) once the last person leaves
    private void RemoveRoomIfEmpty(string room)
    {
        if (DefaultRooms.Contains(room)) return;
        if (_members.Values.Any(m => m.Room == room)) return;
        _history.Remove(room);
    }

    public static string ValidateName(string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || name.Length > MaxNameLength)
            throw new ChatException($"Name must be between 1 and {MaxNameLength} characters.");
        return name;
    }

    public static string ValidateRoom(string? room)
    {
        room = Regex.Replace(room?.Trim() ?? "", @"\s+", " ");
        if (room.Length == 0 || room.Length > MaxRoomLength)
            throw new ChatException($"Room name must be between 1 and {MaxRoomLength} characters.");
        if (!RoomPattern().IsMatch(room))
            throw new ChatException("Room names can only use letters, numbers, spaces and hyphens.");
        return room;
    }

    public static string ValidateMessage(string? text)
    {
        text = text?.Trim() ?? "";
        if (text.Length == 0)
            throw new ChatException("Message can't be empty.");
        if (text.Length > MaxMessageLength)
            throw new ChatException($"Messages are limited to {MaxMessageLength} characters.");
        return text;
    }

    [GeneratedRegex(@"^[\p{L}\p{N} -]+$")]
    private static partial Regex RoomPattern();
}

public record RoomInfo(string Name, int UserCount);

public class ChatException(string message) : Exception(message);
