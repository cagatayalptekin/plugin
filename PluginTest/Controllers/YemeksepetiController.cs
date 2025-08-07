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

        // Yemeksepeti sipariş gönderimi

        [HttpPost("/order/{remoteId}")]
        public async Task<IActionResult> ReceiveOrder(string remoteId, [FromBody] DeliveryHeroOrderModel order)
        {
            if (order == null)
                return BadRequest("Sipariş verisi boş.");

            try
            {
                // 🔹 JSON verisini oluştur
                var orderJson = JsonSerializer.Serialize(order, new JsonSerializerOptions { WriteIndented = true });

                // 🔹 savehere.txt dosyasının yolu
                string filePath = Path.Combine(Directory.GetCurrentDirectory(), "savehere.txt");

                // 🔹 Dosyaya yaz (üstüne ekle)
                await System.IO.File.AppendAllTextAsync(filePath, $"\n\n--- Yeni Sipariş ---\n{orderJson}\n");

                Console.WriteLine($"✅ Sipariş savehere.txt dosyasına yazıldı: {filePath}");

                return Ok(new
                {
                    remoteResponse = new
                    {
                        remoteOrderId = $"POS_{remoteId}_ORDER_{order.Token}"
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Dosyaya yazarken hata oluştu: {ex.Message}");
            }
        }

    }
    public class YemeksepetiOrderModel
    {
        public string OrderId { get; set; }
        public string CustomerName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public List<string> Items { get; set; }
        public string Note { get; set; }
    }
    public class DeliveryHeroOrderModel
    {
        public string Token { get; set; }
        public string Code { get; set; }
        public Comments Comments { get; set; }
        public DateTime CreatedAt { get; set; }
        public Customer Customer { get; set; }
        public Delivery Delivery { get; set; }
        public List<Discount> Discounts { get; set; }
        public string ExpeditionType { get; set; }
        public DateTime ExpiryDate { get; set; }
        public ExtraParameters ExtraParameters { get; set; }
        public InvoicingInformation InvoicingInformation { get; set; }
        public LocalInfo LocalInfo { get; set; }
        public Payment Payment { get; set; }
        public bool Test { get; set; }
        public string ShortCode { get; set; }
        public bool PreOrder { get; set; }
        public Pickup Pickup { get; set; }
        public PlatformRestaurant PlatformRestaurant { get; set; }
        public Price Price { get; set; }
        public List<Product> Products { get; set; }
        public string CorporateTaxId { get; set; }
        public CallbackUrls CallbackUrls { get; set; }
    }
    public class Comments { public string CustomerComment { get; set; } }
    public class Customer { public string Email { get; set; } public string FirstName { get; set; } public string LastName { get; set; } public string MobilePhone { get; set; } }
    public class Delivery { public Address Address { get; set; } public DateTime ExpectedDeliveryTime { get; set; } public bool ExpressDelivery { get; set; } public DateTime? RiderPickupTime { get; set; } }
    public class Address { /* detaylar varsa buraya eklenir */ }
    public class Discount { /* örnek varsa doldururum */ }
    public class ExtraParameters { public string Property1 { get; set; } public string Property2 { get; set; } }
    public class InvoicingInformation { public string CarrierType { get; set; } public string CarrierValue { get; set; } }
    public class LocalInfo { public string CountryCode { get; set; } public string CurrencySymbol { get; set; } public string Platform { get; set; } public string PlatformKey { get; set; } }
    public class Payment { public string Status { get; set; } public string Type { get; set; } }
    public class PlatformRestaurant { public string Id { get; set; } }
    public class Price { public List<object> DeliveryFees { get; set; } public string GrandTotal { get; set; } public string PayRestaurant { get; set; } public string RiderTip { get; set; } public string TotalNet { get; set; } public string VatTotal { get; set; } public string CollectFromCustomer { get; set; } }
    public class Product { /* ürün modeli */ }
    public class Pickup { }
    public class CallbackUrls
    {
        public string OrderAcceptedUrl { get; set; }
        public string OrderRejectedUrl { get; set; }
        public string OrderPickedUpUrl { get; set; }
        public string OrderPreparedUrl { get; set; }
        public string OrderProductModificationUrl { get; set; }
        public string OrderPreparationTimeAdjustmentUrl { get; set; }
    }


}

