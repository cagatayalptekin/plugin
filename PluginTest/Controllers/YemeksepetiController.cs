using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Xml.Linq;

namespace PluginTest.Controllers
{
    [ApiController]
    [Route("api/yemeksepeti")]
    public class YemeksepetiController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
        // Test amaçlı GET
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok("Yemeksepeti plugin aktif.");
        }
        [HttpGet("testlog")]
        public IActionResult TestLog()
        {
            Console.WriteLine("✅ Console log testi çalışıyor.");
            return Ok("Log gönderildi.");
        }
        // Yemeksepeti sipariş gönderimi

        private const string ExpectedApiKey = "X7kL93-fgh8W-Zmq0P-Ak2N9";

        // 1) Senin verdiğin webhook (x-api-key + basit body de gelse kabul)
        [HttpPost("order")]
        public async Task<IActionResult> ReceiveOrderSimplified([FromBody] object body)
        {
            Console.WriteLine("🟡 HIT /api/yemeksepeti/order");

            // x-api-key doğrula (ancak varsa zorla)
            if (!Request.Headers.TryGetValue("x-api-key", out var apiKey) || apiKey != ExpectedApiKey)
            {
                Console.WriteLine("❌ x-api-key invalid/missing");
                return Unauthorized("API key hatalı");
            }

            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });
            var file = Path.Combine(Directory.GetCurrentDirectory(), "savehere.txt");
            await System.IO.File.AppendAllTextAsync(file, $"\n\n--- Yeni Sipariş (YS Basit) ---\n{json}\n");
            Console.WriteLine("💾 savehere.txt yazıldı");

            return Ok(new { status = "ok" });
        }

        // 2) Standart DH endpoint (Bearer varsa kabul et; body büyük şema)
        [HttpPost("/order/{remoteId}")]
        public async Task<IActionResult> ReceiveOrderStandard(string remoteId, [FromBody] object body)
        {
            Console.WriteLine($"🟡 HIT /order/{remoteId}");

            // Bearer varsa logla (zorunlu kılmak istersen burada kontrol et)
            var auth = Request.Headers.Authorization.ToString();
            Console.WriteLine($"🔐 Authorization: {auth}");

            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });
            var file = Path.Combine(Directory.GetCurrentDirectory(), "savehere.txt");
            await System.IO.File.AppendAllTextAsync(file, $"\n\n--- Yeni Sipariş (DH Standart) ---\n{json}\n");
            Console.WriteLine("💾 savehere.txt yazıldı");

            // DH beklenen acknowledge formatı
            // body içinden token'ı parse etmek yerine dummy döndük; istersen token'ı JsonDocument ile çekebiliriz.
            return Ok(new { remoteResponse = new { remoteOrderId = $"POS_{remoteId}_ORDER_ACK" } });
        }

    }
    public class YemeksepetiOrderModel
    {
        public string? OrderId { get; set; }
        public string? CustomerName { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public List<string?> Items { get; set; }
        public string? Note { get; set; }
    }
    public class DeliveryHeroOrderModel
    {
        public string? Token { get; set; }
        public string? Code { get; set; }
        public Comments? Comments { get; set; }
        public DateTime? CreatedAt { get; set; }
        public Customer? Customer { get; set; }
        public Delivery? Delivery { get; set; }
        public List<Discount>? Discounts { get; set; }
        public string? ExpeditionType { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public ExtraParameters? ExtraParameters { get; set; }
        public InvoicingInformation? InvoicingInformation { get; set; }
        public LocalInfo? LocalInfo { get; set; }
        public Payment? Payment { get; set; }
        public bool? Test { get; set; }
        public string? ShortCode { get; set; }
        public bool? PreOrder { get; set; }
        public Pickup? Pickup { get; set; }
        public PlatformRestaurant? PlatformRestaurant { get; set; }
        public Price? Price { get; set; }
        public List<Product>? Products { get; set; }
        public string? CorporateTaxId { get; set; }
        public CallbackUrls? CallbackUrls { get; set; }
    }
    public class Comments { public string? CustomerComment { get; set; } }
    public class Customer { public string Email { get; set; } public string? FirstName { get; set; } public string? LastName { get; set; } public string? MobilePhone { get; set; } }
    public class Delivery { public Address? Address { get; set; } public DateTime? ExpectedDeliveryTime { get; set; } public bool? ExpressDelivery { get; set; } public DateTime? RiderPickupTime { get; set; } }
    public class Address { /* detaylar varsa buraya eklenir */ }
    public class Discount { /* örnek varsa doldururum */ }
    public class ExtraParameters { public string? Property1 { get; set; } public string? Property2 { get; set; } }
    public class InvoicingInformation { public string? CarrierType { get; set; } public string? CarrierValue { get; set; } }
    public class LocalInfo { public string? CountryCode { get; set; } public string? CurrencySymbol { get; set; } public string? Platform { get; set; } public string? PlatformKey { get; set; } }
    public class Payment { public string? Status { get; set; } public string? Type { get; set; } }
    public class PlatformRestaurant { public string? Id { get; set; } }
    public class Price { public List<object>? DeliveryFees { get; set; } public string? GrandTotal { get; set; } public string? PayRestaurant { get; set; } public string? RiderTip { get; set; } public string? TotalNet { get; set; } public string? VatTotal { get; set; } public string? CollectFromCustomer { get; set; } }
    public class Product { /* ürün modeli */ }
    public class Pickup { }
    public class CallbackUrls
    {
        public string? OrderAcceptedUrl { get; set; }
        public string? OrderRejectedUrl { get; set; }
        public string? OrderPickedUpUrl { get; set; }
        public string? OrderPreparedUrl { get; set; }
        public string? OrderProductModificationUrl { get; set; }
        public string? OrderPreparationTimeAdjustmentUrl { get; set; }
    }


}

