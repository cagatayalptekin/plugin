using QuestPDF.Infrastructure;

namespace PluginTest
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.EnableDebugging = true;

            // Controllers
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // 🔴 SSE publish için singleton kanal
            builder.Services.AddSingleton<OrderStream>();

            // CORS
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAll", policy =>
                {
                    policy
                        .AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });

            var port = Environment.GetEnvironmentVariable("PORT") ?? "8080"; // ← (typo fix: sondaki 'a' kalktı)
            builder.WebHost.UseUrls($"http://*:{port}");

            var app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseCors("AllowAll");
            app.UseAuthorization();

            // Angular statikleri
            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.MapControllers();
            app.MapFallbackToFile("index.html"); // /api dışındaki tüm yollar Angular'a düşer

            app.Run();
        }
    }

    // 🔴 Basit SSE kanalı (string JSON taşır)
    public class OrderStream
    {
        private readonly System.Threading.Channels.Channel<string> _channel =
            System.Threading.Channels.Channel.CreateUnbounded<string>();

        public System.Threading.Channels.ChannelReader<string> Reader => _channel.Reader;

        public ValueTask PublishAsync(string message) => _channel.Writer.WriteAsync(message);
    }
}
