// Controllers/EventsController.cs
using Microsoft.AspNetCore.Mvc;
using PluginTest.Infrastructure;

namespace PluginTest.Controllers;

[ApiController]
[Route("api/orders")]
public class EventsController : ControllerBase
{
    private readonly OrderStream _orderStream;

    public EventsController(OrderStream orderStream)
    {
        _orderStream = orderStream;
    }

    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        Response.StatusCode = 200;
        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var (id, reader) = _orderStream.Subscribe();
        try
        {
            // Tarayıcının auto-reconnect süresi (ms)
            await Response.WriteAsync("retry: 5000\n\n", ct);
            await Response.Body.FlushAsync(ct);

            await foreach (var msg in reader.ReadAllAsync(ct))
            {
                await Response.WriteAsync("event: new-order\n", ct);
                await Response.WriteAsync($"data: {msg}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        finally
        {
            _orderStream.Unsubscribe(id);
        }
    }
}
