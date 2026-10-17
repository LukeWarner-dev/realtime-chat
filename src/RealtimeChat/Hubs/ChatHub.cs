using Microsoft.AspNetCore.SignalR;
using RealtimeChat.Chat;

namespace RealtimeChat.Hubs;

/// <summary>Methods the server can call on connected browsers.</summary>
public interface IChatClient
{
    Task ReceiveMessage(ChatMessage message);
    Task UsersChanged(string room, IReadOnlyList<string> users);
    Task RoomsChanged(IReadOnlyList<RoomInfo> rooms);
    Task UserTyping(string userName, bool isTyping);
}

public class ChatHub(ChatState state, ILogger<ChatHub> logger) : Hub<IChatClient>
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.RoomsChanged(state.GetRooms());
        await base.OnConnectedAsync();
    }

    public async Task<JoinResult> Join(string userName, string room)
    {
        try
        {
            userName = ChatState.ValidateName(userName);
            room = ChatState.ValidateRoom(room);

            var current = state.GetMember(Context.ConnectionId);
            if (current is not null && string.Equals(current.Room, room, StringComparison.OrdinalIgnoreCase))
                return new JoinResult(current.Room, state.GetHistory(current.Room), state.GetUsers(current.Room));

            var previous = state.Join(Context.ConnectionId, userName, room);
            room = state.GetMember(Context.ConnectionId)!.Room; // the stored casing

            if (previous is not null)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, previous);
                await Announce(previous, $"{userName} left the room");
                await Clients.Group(previous).UsersChanged(previous, state.GetUsers(previous));
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, room);
            await Announce(room, $"{userName} joined the room");
            await Clients.Group(room).UsersChanged(room, state.GetUsers(room));
            await Clients.All.RoomsChanged(state.GetRooms());

            logger.LogInformation("{User} joined {Room}", userName, room);
            return new JoinResult(room, state.GetHistory(room), state.GetUsers(room));
        }
        catch (ChatException ex)
        {
            // HubException is the only exception type whose message reaches the client
            throw new HubException(ex.Message);
        }
    }

    public async Task SendMessage(string text)
    {
        var member = state.GetMember(Context.ConnectionId)
            ?? throw new HubException("Join a room before sending messages.");

        string clean;
        try
        {
            clean = ChatState.ValidateMessage(text);
        }
        catch (ChatException ex)
        {
            throw new HubException(ex.Message);
        }

        var message = ChatMessage.FromUser(member.Room, member.UserName, clean);
        state.AddMessage(message);
        await Clients.Group(member.Room).ReceiveMessage(message);
    }

    public async Task Typing(bool isTyping)
    {
        var member = state.GetMember(Context.ConnectionId);
        if (member is null) return;
        await Clients.OthersInGroup(member.Room).UserTyping(member.UserName, isTyping);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var member = state.Leave(Context.ConnectionId);
        if (member is not null)
        {
            await Clients.OthersInGroup(member.Room).UserTyping(member.UserName, false);
            await Announce(member.Room, $"{member.UserName} left the room");
            await Clients.Group(member.Room).UsersChanged(member.Room, state.GetUsers(member.Room));
            await Clients.All.RoomsChanged(state.GetRooms());
        }
        await base.OnDisconnectedAsync(exception);
    }

    private async Task Announce(string room, string text)
    {
        var message = ChatMessage.System(room, text);
        state.AddMessage(message);
        await Clients.Group(room).ReceiveMessage(message);
    }
}
