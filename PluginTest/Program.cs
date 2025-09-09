using PluginTest.Infrastructure;
using PluginTest.Options;
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

            builder.Services.AddSingleton<OrderStream>();
            builder.Services.AddMemoryCache();
            builder.Services.AddHttpClient(); // genel fabrika

            builder.Services.Configure<GetirOptions>(builder.Configuration.GetSection("Getir"));
            // "Yemeksepeti": { "LoginUrl": "...", "Username": "...", "Password": "..." }
            builder.Services.Configure<YemeksepetiOptions>(builder.Configuration.GetSection("Yemeksepeti"));


            // CORS
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AppOnly", policy =>
                {
                    policy.WithOrigins(
                        "https://plugin-4.onrender.com",
                        "http://localhost:4200"
                    )
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials(); // gerekirse
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

            app.UseCors("AppOnly");
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
 
}
