using WebSocketsSample;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IChatWebSocketPoolFactory, ChatWebSocketPoolFactory>();

var app = builder.Build();

app.UseWebSockets();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();