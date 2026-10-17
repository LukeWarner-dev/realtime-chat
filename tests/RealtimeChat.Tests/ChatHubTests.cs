using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using RealtimeChat.Chat;

namespace RealtimeChat.Tests;

/// <summary>
/// Runs the real app in memory and connects to the hub the same way the
/// browser does, so these test the whole round trip through SignalR.
/// </summary>
public class ChatHubTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<HubConnection> _connections = [];

    public ChatHubTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private async Task<HubConnection> ConnectAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/chat"),
                options => options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler())
            .Build();
        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    // collects user messages pushed to a connection so a test can await the next one
    private static ChannelReader<ChatMessage> CollectMessages(HubConnection connection)
    {
        var channel = Channel.CreateUnbounded<ChatMessage>();
        connection.On<ChatMessage>("ReceiveMessage", m =>
        {
            if (!m.IsSystem) channel.Writer.TryWrite(m);
        });
        return channel.Reader;
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Message_is_delivered_to_everyone_in_the_room()
    {
        var alice = await ConnectAsync();
        var bob = await ConnectAsync();
        var bobInbox = CollectMessages(bob);
        var room = Unique("Room");

        await alice.InvokeAsync<JoinResult>("Join", Unique("alice"), room);
        await bob.InvokeAsync<JoinResult>("Join", Unique("bob"), room);
        await alice.InvokeAsync("SendMessage", "hello bob");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await bobInbox.ReadAsync(timeout.Token);

        Assert.Equal("hello bob", received.Text);
        Assert.Equal(room, received.Room);
    }

    [Fact]
    public async Task Messages_do_not_leak_into_other_rooms()
    {
        var alice = await ConnectAsync();
        var carol = await ConnectAsync();
        var carolInbox = CollectMessages(carol);

        await alice.InvokeAsync<JoinResult>("Join", Unique("alice"), Unique("RoomA"));
        await carol.InvokeAsync<JoinResult>("Join", Unique("carol"), Unique("RoomB"));
        await alice.InvokeAsync("SendMessage", "only for room A");

        await Task.Delay(300);
        Assert.False(carolInbox.TryRead(out _));
    }

    [Fact]
    public async Task Joining_returns_recent_history_and_users()
    {
        var alice = await ConnectAsync();
        var bob = await ConnectAsync();
        var room = Unique("Room");
        var aliceName = Unique("alice");

        await alice.InvokeAsync<JoinResult>("Join", aliceName, room);
        await alice.InvokeAsync("SendMessage", "first!");

        var result = await bob.InvokeAsync<JoinResult>("Join", Unique("bob"), room);

        Assert.Contains(result.History, m => m.Text == "first!" && m.User == aliceName);
        Assert.Contains(aliceName, result.Users);
        Assert.Equal(2, result.Users.Count);
    }

    [Fact]
    public async Task Sending_before_joining_is_rejected()
    {
        var connection = await ConnectAsync();

        var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("SendMessage", "hi"));
        Assert.Contains("Join a room", ex.Message);
    }

    [Fact]
    public async Task Duplicate_names_are_rejected()
    {
        var first = await ConnectAsync();
        var second = await ConnectAsync();
        var name = Unique("sam");

        await first.InvokeAsync<JoinResult>("Join", name, "General");
        var ex = await Assert.ThrowsAsync<HubException>(() =>
            second.InvokeAsync<JoinResult>("Join", name.ToUpperInvariant(), "General"));

        Assert.Contains("already in use", ex.Message);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
