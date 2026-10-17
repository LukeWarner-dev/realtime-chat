using RealtimeChat.Chat;

namespace RealtimeChat.Tests;

public class ChatStateTests
{
    private readonly ChatState _state = new();

    [Fact]
    public void Starts_with_the_default_rooms()
    {
        var rooms = _state.GetRooms().Select(r => r.Name);
        Assert.Equal(ChatState.DefaultRooms, rooms);
    }

    [Fact]
    public void Join_adds_the_user_to_the_room()
    {
        _state.Join("c1", "Luke", "General");

        Assert.Equal(["Luke"], _state.GetUsers("General"));
        Assert.Equal(new ChatState.Member("Luke", "General"), _state.GetMember("c1"));
    }

    [Fact]
    public void Names_must_be_unique_ignoring_case()
    {
        _state.Join("c1", "Luke", "General");

        var ex = Assert.Throws<ChatException>(() => _state.Join("c2", "luke", "Random"));
        Assert.Contains("already in use", ex.Message);
    }

    [Fact]
    public void Joining_another_room_moves_the_user_and_returns_the_old_room()
    {
        _state.Join("c1", "Luke", "General");
        var previous = _state.Join("c1", "Luke", "Random");

        Assert.Equal("General", previous);
        Assert.Empty(_state.GetUsers("General"));
        Assert.Equal(["Luke"], _state.GetUsers("Random"));
    }

    [Fact]
    public void Room_names_match_existing_rooms_ignoring_case()
    {
        _state.Join("c1", "Luke", "general");

        Assert.Equal("General", _state.GetMember("c1")!.Room);
    }

    [Fact]
    public void Custom_rooms_are_removed_when_the_last_person_leaves()
    {
        _state.Join("c1", "Luke", "Gaming");
        Assert.Contains(_state.GetRooms(), r => r.Name == "Gaming");

        _state.Leave("c1");

        Assert.DoesNotContain(_state.GetRooms(), r => r.Name == "Gaming");
    }

    [Fact]
    public void Default_rooms_are_never_removed()
    {
        _state.Join("c1", "Luke", "Help");
        _state.Leave("c1");

        Assert.Contains(_state.GetRooms(), r => r.Name == "Help");
    }

    [Fact]
    public void History_keeps_only_the_most_recent_messages()
    {
        for (var i = 0; i < ChatState.HistoryLimit + 10; i++)
            _state.AddMessage(ChatMessage.FromUser("General", "Luke", $"message {i}"));

        var history = _state.GetHistory("General");

        Assert.Equal(ChatState.HistoryLimit, history.Count);
        Assert.Equal("message 10", history[0].Text);
        Assert.Equal($"message {ChatState.HistoryLimit + 9}", history[^1].Text);
    }

    [Fact]
    public void Room_list_includes_user_counts()
    {
        _state.Join("c1", "Luke", "General");
        _state.Join("c2", "Sam", "General");

        Assert.Equal(2, _state.GetRooms().Single(r => r.Name == "General").UserCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("this name is far too long for the limit")]
    public void Invalid_names_are_rejected(string name)
    {
        Assert.Throws<ChatException>(() => ChatState.ValidateName(name));
    }

    [Theory]
    [InlineData("<script>")]
    [InlineData("room/../x")]
    [InlineData("")]
    public void Invalid_room_names_are_rejected(string room)
    {
        Assert.Throws<ChatException>(() => ChatState.ValidateRoom(room));
    }

    [Fact]
    public void Room_names_are_trimmed_and_spaces_collapsed()
    {
        Assert.Equal("Study Group", ChatState.ValidateRoom("  Study    Group "));
    }

    [Fact]
    public void Messages_over_the_limit_are_rejected()
    {
        var text = new string('a', ChatState.MaxMessageLength + 1);
        Assert.Throws<ChatException>(() => ChatState.ValidateMessage(text));
    }
}
