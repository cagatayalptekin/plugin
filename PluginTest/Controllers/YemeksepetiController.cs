using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace PluginTest.Controllers
{
    [ApiController]
    [Route("api/yemeksepeti")]
    public class YemeksepetiController : Controller
    {
        public List<string> OrderIdentifiers;
        public string token { get; set; }
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
        [HttpPost("order/{remoteId}")]
        public IActionResult PostOrder([FromRoute] string remoteId, [FromBody] YemeksepetiOrderModel order)
        {
            Console.WriteLine("Siparişi Gördüm");
            Console.WriteLine("buraya girdim");

            string orderJson = JsonSerializer.Serialize(order, new JsonSerializerOptions { WriteIndented = true });

            // JSON string'ini konsola yazdırın
            Console.WriteLine(orderJson);
            // 1. Yetkilendirme (Authorization)
            // JWT Auth kontrolü burada yapılmalıdır.
            // Middleware'de halledilebilir.

            // 2. Hızlı Doğrulama (Quick Validation)
            // Gelen verinin basic olarak geçerli olup olmadığını kontrol edin.
            if (order == null || string.IsNullOrEmpty(order.token) || string.IsNullOrEmpty(remoteId))
            {
                // Eğer istek gövdesi veya kritik alanlar eksikse 400 Bad Request döndürün.
                return BadRequest(new { error = "Invalid request payload." });
            }

            // 3. Siparişi Kaydetme (Persist the Order)
            // Veritabanına veya başka bir kalıcı depolama alanına siparişi kaydedin.
            // Bu işlem genellikle bir servis katmanı (service layer) tarafından yapılır.
            try
            {
                // Örneğin: _orderService.SaveOrder(order, remoteId);
                // Burada siparişin kaydedilmesi için gerekli kodlar yer alacak.

                // 4. Sipariş Tipini Belirleme (Identify Order Type)
                string orderType = GetOrderType(order);
                // Sipariş tipine göre farklı işlemler yapılabilir.

                // 5. Asenkron İşleme (Asynchronous Processing)
                // Siparişi kaydettikten sonra, asıl işleme (mutfağa gönderme, stok kontrolü vb.)
                // asenkron olarak devam edin. Bu sayede HTTP isteği hemen yanıtlanmış olur.
                // Örneğin: Task.Run(() => _orderProcessingService.ProcessOrderAsync(order));
            }
            catch (Exception ex)
            {
                // Kaydetme sırasında bir hata oluşursa 500 Internal Server Error döndürün.
                // Middleware'de global hata yönetimi de kullanılabilir.
                return StatusCode(500, new { error = "An internal server error occurred.", details = ex.Message });
            }

            // 6. Başarılı Yanıt (Acknowledge Order)
            // Her şey yolundaysa, siparişin alındığını belirten başarılı bir yanıt döndürün.
            // Dokümantasyonda belirtildiği gibi 200 veya 202 kullanılabilir.
            // 202 Accepted, asenkron işlemin başlatıldığını belirtmek için daha uygundur.
            UpdateStatus(order.token);
            return Accepted(new { remoteResponse = new { remoteOrderId = $"YEMEKSEPETI_ORDER_{order.code}" } });
        }
        [HttpPost("order")]
        public IActionResult PostOrder([FromBody] YemeksepetiOrderModel order)
        {
            Console.WriteLine("Siparişi Gördüm");
            // 1. Yetkilendirme (Authorization)
            // JWT Auth kontrolü burada yapılmalıdır.
            // Middleware'de halledilebilir.
            Console.WriteLine(order);
            // 2. Hızlı Doğrulama (Quick Validation)
            // Gelen verinin basic olarak geçerli olup olmadığını kontrol edin.


            // 3. Siparişi Kaydetme (Persist the Order)
            // Veritabanına veya başka bir kalıcı depolama alanına siparişi kaydedin.
            // Bu işlem genellikle bir servis katmanı (service layer) tarafından yapılır.
            try
            {
                // Örneğin: _orderService.SaveOrder(order, remoteId);
                // Burada siparişin kaydedilmesi için gerekli kodlar yer alacak.

                // 4. Sipariş Tipini Belirleme (Identify Order Type)
                string orderType = GetOrderType(order);
                // Sipariş tipine göre farklı işlemler yapılabilir.

                // 5. Asenkron İşleme (Asynchronous Processing)
                // Siparişi kaydettikten sonra, asıl işleme (mutfağa gönderme, stok kontrolü vb.)
                // asenkron olarak devam edin. Bu sayede HTTP isteği hemen yanıtlanmış olur.
                // Örneğin: Task.Run(() => _orderProcessingService.ProcessOrderAsync(order));
            }
            catch (Exception ex)
            {
                // Kaydetme sırasında bir hata oluşursa 500 Internal Server Error döndürün.
                // Middleware'de global hata yönetimi de kullanılabilir.
                return StatusCode(500, new { error = "An internal server error occurred.", details = ex.Message });
            }

            // 6. Başarılı Yanıt (Acknowledge Order)
            // Her şey yolundaysa, siparişin alındığını belirten başarılı bir yanıt döndürün.
            // Dokümantasyonda belirtildiği gibi 200 veya 202 kullanılabilir.
            // 202 Accepted, asenkron işlemin başlatıldığını belirtmek için daha uygundur.
            UpdateStatus(order.token);// Asenkron metodu bekletiyoruz, gerçek uygulamada Task.Run kullanılabilir.
            return Accepted(new { remoteResponse = new { remoteOrderId = $"YEMEKSEPETI_ORDER_{order.code}" } });
        }

       
        public async Task<IActionResult> UpdateStatus(string orderToken)
        {

            //      remoteId = "agrabt123";
            using (HttpClient client = new HttpClient())
            {

                var content = new FormUrlEncodedContent(new[]
            {
    new KeyValuePair<string, string>("username", "me-tr-plugin-agra-bilgi-teknolojileri-sanayi-ve-ticaret-limited-sirketi-001"),
    new KeyValuePair<string, string>("password", "KIOLr6UqHh"),
    new KeyValuePair<string, string>("grant_type", "client_credentials")
});

                var response = await client.PostAsync("https://integration-middleware-tr.me.restaurant-partners.com/v2/login", content);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var responseBody = await response.Content.ReadAsStringAsync();

                // Token modelini deserialize et
                var loginResult = JsonSerializer.Deserialize<YemeksepetiLoginResponse>(responseBody);
                token = loginResult?.access_token; // token field'a ata


            }



            using (HttpClient client = new HttpClient())
            {
                var chainCode = "1X7UTqDJ"; // Örnek kod, gerçek kodu buraya girin

              //  client.DefaultRequestHeaders.Add("token", token); // ← Doğru olan bu



              
                //         client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var url = $"https://integration-middleware-tr.me.restaurant-partners.com/v2/order/status/{orderToken}";

                   client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var orderStatusRequest = new OrderStatusUpdateRequest
                {
                    acceptanceTime = "2016-10-05T00:00:00+05:00",
                    status = "order_accepted",
               
                };

                // Serialize the object to JSON
                string jsonPayload = JsonSerializer.Serialize(orderStatusRequest);

                // Prepare the content to send in the POST request (application/json)
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Send the POST request asynchronously
                var response = await client.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Request successful.");
                    var responseContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine("Response: " + responseContent);
                }
                else
                {
                    Console.WriteLine($"Request failed. Status Code: {response.StatusCode}");
                }

            
                return Ok();
            }
        }




        // Dokümantasyondaki JavaScript fonksiyonuna benzer bir C# metodu
        private string GetOrderType(YemeksepetiOrderModel order)
        {
            if (order.expeditionType == "pickup")
            {
                return "Pickup";
            }

            if (order.expeditionType == "delivery")
            {
                if (order.delivery?.riderPickupTime == null)
                {
                    return "VendorDelivery";
                }
                return "OwnDelivery";
            }

            return "Unknown";
        }

    }
 

    public class Modifications
    {
        public List<object> Products { get; set; }
    }
    public class OrderIdentifiersResponse
    {
        [JsonPropertyName("orderIdentifiers")]
        public List<string> OrderIdentifiers { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
    public class YemeksepetiLoginResponse
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
    }
    public class YemeksepetiOrderModel
    {
        // Required fields
        public string token { get; set; } = String.Empty;
        public string code { get; set; } = String.Empty;
        public Comments? comments { get; set; }
        public DateTime? createdAt { get; set; }
        public Customer? customer { get; set; }
        public List<Discount>? discounts { get; set; }
        public string expeditionType { get; set; } = String.Empty;
        public DateTime? expiryDate { get; set; }
        public LocalInfo? localInfo { get; set; }
        public Payment? payment { get; set; }
        public bool? test { get; set; }
        public bool? preOrder { get; set; }
        public PlatformRestaurant? platformRestaurant { get; set; }
        public Price? price { get; set; }
        public List<Product>? products { get; set; }
        public string? corporateTaxId { get; set; }
        public CallbackUrls? callbackUrls { get; set; }

        // Optional fields
        public Delivery? delivery { get; set; }
        public ExtraParameters? extraParameters { get; set; }
        public InvoicingInformation? invoicingInformation { get; set; }
        public string? shortCode { get; set; }
        public Pickup? pickup { get; set; }
        public PreparationTimeAdjustments? preparationTimeAdjustments { get; set; }
    }

    public class Comments
    {
        public string? customerComment { get; set; }
    }

    public class Customer
    {
        public string? email { get; set; }
        public string? firstName { get; set; }
        public string? lastName { get; set; }
        public string? mobilePhone { get; set; }
        public List<string>? flags { get; set; }
    }

    public class Delivery
    {
        public Address? address { get; set; }
        public DateTime? expectedDeliveryTime { get; set; }
        public bool? expressDelivery { get; set; }
        public DateTime? riderPickupTime { get; set; }
    }

    public class Address
    {
        public string? postcode { get; set; }
        public string? city { get; set; }
        public string? street { get; set; }
        public string? number { get; set; }
    }

    public class Discount
    {
        public string? name { get; set; }
        public string? amount { get; set; }
        public List<Sponsorship>? sponsorships { get; set; }
    }

    public class Sponsorship
    {
        public string? sponsor { get; set; }
        public string? amount { get; set; }
    }

    public class ExtraParameters
    {
        public Dictionary<string, string>? properties { get; set; }
    }

    public class InvoicingInformation
    {
        public string? carrierType { get; set; }
        public string? carrierValue { get; set; }
    }

    public class LocalInfo
    {
        public string? countryCode { get; set; }
        public string? currencySymbol { get; set; }
        public string? platform { get; set; }
        public string? platformKey { get; set; }
    }

    public class Payment
    {
        public string? status { get; set; }
        public string? type { get; set; }
    }

    public class Pickup
    {
        public DateTime? pickupTime { get; set; }
        public string? pickupCode { get; set; }
    }

    public class PlatformRestaurant
    {
        public string? id { get; set; }
    }

    public class Price
    {
        // Required fields
        public List<DeliveryFee>? deliveryFees { get; set; }
        public string? grandTotal { get; set; }
        public string? totalNet { get; set; }
        public string? vatTotal { get; set; }

        // Optional fields
        public string? payRestaurant { get; set; }
        public string? riderTip { get; set; }
        public string? collectFromCustomer { get; set; }
    }

    public class DeliveryFee
    {
        public string? name { get; set; }
        public double? value { get; set; }
    }

    public class Product
    {
        public string? categoryName { get; set; }
        public string? name { get; set; }
        public string? paidPrice { get; set; }
        public string? quantity { get; set; }
        public string? remoteCode { get; set; }
        public List<SelectedTopping>? selectedToppings { get; set; }
        public string? unitPrice { get; set; }
        public string? comment { get; set; }
        public string? id { get; set; }
        public string? itemUnavailabilityHandling { get; set; }
        public Variation? variation { get; set; }
        public List<Discount>? discounts { get; set; }
    }

    public class SelectedTopping
    {
        public List<object>? children { get; set; }
        public string? name { get; set; }
        public string? price { get; set; }
        public int? quantity { get; set; }
        public string? id { get; set; }
        public string? remoteCode { get; set; }
        public string? type { get; set; }
        public string? itemUnavailabilityHandling { get; set; }
        public List<Discount>?   discounts { get; set; }
    }

    public class Variation
    {
        public string? name { get; set; }
    }

    public class PreparationTimeAdjustments
    {
        public DateTime? maxPickUpTimestamp { get; set; }
        public DateTime? minPickupTimestamp { get; set; }
        public List<int>? preparationTimeChangeIntervalsInMinutes { get; set; }
    }

    public class CallbackUrls
    {
        public string? orderAcceptedUrl { get; set; }
        public string? orderRejectedUrl { get; set; }
        public string? orderPickedUpUrl { get; set; }
        public string? orderPreparedUrl { get; set; }
        public string? orderProductModificationUrl { get; set; }
        public string? orderPreparationTimeAdjustmentUrl { get; set; }
    }
    public class OrderStatusUpdateRequest
    {
        public string status { get; set; }                    // "order_accepted" | "order_rejected" | "order_picked_up"
        public string acceptanceTime { get; set; }            // ISO-8601, required for order_accepted
        public string? remoteOrderId { get; set; }             // your POS-side order id (optional but recommended)
        public Modifications? modifications { get; set; }      // optional (only with order_accepted)
    }

  
}

