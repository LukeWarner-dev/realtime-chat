using RealtimeChat.Chat;
using RealtimeChat.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 8 * 1024; // messages are capped at 500 chars anyway
});
builder.Services.AddSingleton<ChatState>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

// lets the integration tests start the app with WebApplicationFactory
public partial class Program;
