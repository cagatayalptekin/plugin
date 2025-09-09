using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PluginTest.Infrastructure;
using PluginTest;                    // OrderStream için
using System.Threading.Channels;     // (gerek duyulabilir)
using PluginTest.Options;
namespace PluginTest.Controllers;
using Microsoft.Extensions.Options;

[ApiController]
[Route("api/yemeksepeti")]
public class YemeksepetiController : Controller
{
     
    public List<string> OrderIdentifiers;
    public static List<YemeksepetiOrderModel> orders = new();
    public string token { get; set; }

    private readonly OrderStream _orderStream;
    private readonly IHttpClientFactory _http;
    private readonly YemeksepetiOptions _opt;

    public YemeksepetiController(
        OrderStream orderStream,
        IOptions<YemeksepetiOptions> opt,
        IHttpClientFactory http)
    {
        _orderStream = orderStream;
        _http = http;
        _opt = opt.Value;
    }
    [HttpPost("order/{remoteId}")]
    public IActionResult PostOrder([FromRoute] string remoteId, [FromBody] JsonElement order)
    {
        if (string.IsNullOrWhiteSpace(remoteId) || order.ValueKind == JsonValueKind.Null)
            return BadRequest("Eksik veri.");

        var code = order.TryGetProperty("code", out var c) ? c.GetString() : null;
        var token = order.TryGetProperty("token", out var tk) ? tk.GetString() : null;
        decimal? total = null;
        if (order.TryGetProperty("price", out var p) && p.TryGetProperty("grandTotal", out var gt) && gt.TryGetDecimal(out var dec))
            total = dec;

        var payload = JsonSerializer.Serialize(new
        {
            source = "Yemeksepeti",
            kind = "new",
            code = code ?? token ?? remoteId,
            total,
            at = DateTime.UtcNow
        });

        _orderStream.Publish(payload);
        return Ok(new { ok = true });
    }
    public IActionResult Index() => View();

    [HttpGet("ping")]
    public IActionResult Ping() => Ok("Yemeksepeti plugin aktif.");

    [HttpGet("testlog")]
    public IActionResult TestLog()
    {
        Console.WriteLine("✅ Console log testi çalışıyor.");
        return Ok("Log gönderildi.");
    }

    private const string LoginUrl = "https://integration-middleware-tr.me.restaurant-partners.com/v2/login";
    private const string MWBase = "https://integration-middleware-tr.me.restaurant-partners.com";
    private const string MWUser = "me-tr-plugin-agra-bilgi-teknolojileri-sanayi-ve-ticaret-limited-sirketi-001";
    private const string MWPass = "KIOLr6UqHh";

    // === Ortak: Token al ===
    private async Task<string> GetBearerAsync()
    {
        using var client = new HttpClient();
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("username", MWUser),
            new KeyValuePair<string, string>("password", MWPass),
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
        });

        var resp = await client.PostAsync(LoginUrl, content);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Login failed: {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}");

        var loginBody = await resp.Content.ReadAsStringAsync();
        var login = JsonSerializer.Deserialize<YemeksepetiLoginResponse>(loginBody);
        if (string.IsNullOrWhiteSpace(login?.access_token))
            throw new Exception("Token alınamadı.");

        return login.access_token;
    }

    [HttpPost("auth/login")]
    public async Task<IActionResult> Login()
    {
        try
        {
            var tk = await GetBearerAsync();
            token = tk;
            return Ok(new { token = tk });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }

    // === Yemeksepeti Order webhook ===
    

    // 🔴 SSE endpoint (FE: /api/yemeksepeti/orders/stream)
     

    // === Order Status Update ===
    [HttpPost("update-status/{code}/{status}")]
    public async Task<IActionResult> UpdateStatus([FromRoute] string code, [FromRoute] string status, [FromBody] UpdateStatusBody body)
    {
        string bearer;
        try { bearer = await GetBearerAsync(); } catch (Exception ex) { return StatusCode(500, ex.Message); }

        var orderToken = orders?.FirstOrDefault(x => x.code == code)?.token ?? code;
        var url = $"{MWBase}/v2/order/status/{orderToken}";

        object orderStatusRequest;
        status = (status ?? "").Trim().ToLowerInvariant();

        if (status == "accepted")
        {
            var acceptanceTime = !string.IsNullOrWhiteSpace(body?.acceptanceTime)
                ? body!.acceptanceTime
                : DateTime.UtcNow.AddMinutes(15).ToString("o");

            orderStatusRequest = new
            {
                status = "order_accepted",
                acceptanceTime,
                remoteOrderId = string.IsNullOrWhiteSpace(body?.remoteOrderId) ? code : body!.remoteOrderId,
                modifications = body?.modifications
            };
        }
        else if (status == "rejected")
        {
            var reason = !string.IsNullOrWhiteSpace(body?.reason) ? body!.reason : body?.rejectionReasonId;
            var message = !string.IsNullOrWhiteSpace(body?.message) ? body!.message : body?.rejectionNote;

            if (string.IsNullOrWhiteSpace(reason))
                return BadRequest("order_rejected için 'reason' (enum) zorunludur.");

            orderStatusRequest = new
            {
                status = "order_rejected",
                reason,
                message = string.IsNullOrWhiteSpace(message) ? null : message
            };
        }
        else if (status == "pickedup")
        {
            orderStatusRequest = new { status = "order_picked_up" };
        }
        else
        {
            return BadRequest("Invalid status provided. Allowed: accepted | rejected | pickedup");
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var jsonPayload = JsonSerializer.Serialize(orderStatusRequest, jsonOptions);
        var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await client.PostAsync(url, httpContent);
        var respBody = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, respBody);

        return Ok(respBody);
    }

    // === Availability (GET proxy) ===
    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability([FromQuery] string chainCode, [FromQuery] string posVendorId)
    {
        if (string.IsNullOrWhiteSpace(chainCode) || string.IsNullOrWhiteSpace(posVendorId))
            return BadRequest("chainCode ve posVendorId zorunludur.");

        string bearer;
        try { bearer = await GetBearerAsync(); } catch (Exception ex) { return StatusCode(500, ex.Message); }

        var url = $"{MWBase}/v2/chains/{chainCode}/remoteVendors/{posVendorId}/availability";
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        var resp = await client.GetAsync(url);
        if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            return StatusCode(204);

        var raw = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, raw);

        var data = JsonSerializer.Deserialize<List<AvailabilityItem>>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        return Ok(data);
    }

    // === Availability (SET proxy) ===
    [HttpPost("availability")]
    public async Task<IActionResult> SetAvailability([FromBody] SetAvailabilityBody body)
    {
        if (body == null)
            return BadRequest("Body zorunludur.");

        if (string.IsNullOrWhiteSpace(body.chainCode) ||
            string.IsNullOrWhiteSpace(body.posVendorId) ||
            string.IsNullOrWhiteSpace(body.availabilityState) ||
            string.IsNullOrWhiteSpace(body.platformKey) ||
            string.IsNullOrWhiteSpace(body.platformRestaurantId))
        {
            return BadRequest("chainCode, posVendorId, availabilityState, platformKey, platformRestaurantId zorunludur.");
        }

        var state = body.availabilityState.Trim().ToUpperInvariant();
        if (state is "CLOSED" or "CLOSED_UNTIL")
        {
            if (string.IsNullOrWhiteSpace(body.closedReason))
                return BadRequest("closedReason zorunludur (CLOSED/CLOSED_UNTIL).");
            if (state == "CLOSED_UNTIL" && body.closingMinutes is null)
                return BadRequest("closingMinutes zorunludur (CLOSED_UNTIL).");
        }

        string bearer;
        try { bearer = await GetBearerAsync(); } catch (Exception ex) { return StatusCode(500, ex.Message); }

        object prIdObj;
        if (long.TryParse(body.platformRestaurantId, out var prNum))
            prIdObj = prNum;
        else
            prIdObj = body.platformRestaurantId;

        var payload = new Dictionary<string, object?>
        {
            ["availabilityState"] = state,
            ["platformKey"] = body.platformKey,
            ["platformRestaurantId"] = prIdObj
        };
        if (state is "CLOSED" or "CLOSED_UNTIL")
            payload["closedReason"] = body.closedReason;
        if (state == "CLOSED_UNTIL" && body.closingMinutes is not null)
            payload["closingMinutes"] = body.closingMinutes;

        var url = $"{MWBase}/v2/chains/{body.chainCode}/remoteVendors/{body.posVendorId}/availability";

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var resp = await client.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
            return StatusCode((int)resp.StatusCode, raw);

        return Ok(new { ok = true });
    }

    // === Yardımcı: Order Type ===
    private string GetOrderType(YemeksepetiOrderModel order)
    {
        if (order.expeditionType == "pickup") return "Pickup";

        if (order.expeditionType == "delivery")
        {
            if (order.delivery?.riderPickupTime == null) return "VendorDelivery";
            return "OwnDelivery";
        }
        return "Unknown";
    }

    // === Demo: FE sipariş listesi ===
    [HttpPost("get-orders")]
    public async Task<List<YemeksepetiOrderModel>> GetOrders()
    {
        Console.WriteLine("im here get orders");
        foreach (var order in orders)
        {
            Console.WriteLine(order.customer?.firstName);
            Console.WriteLine(order.customer?.lastName);
        }
        return orders;
    }

    // === Opsiyonel: Middleware’den eski siparişleri çekme ===
    [HttpPost("get-last-orders")]
    public async Task<List<YemeksepetiOrderModel>> GetLastOrders()
    {
        string bearer;
        try { bearer = await GetBearerAsync(); } catch (Exception ex) { Console.WriteLine(ex.Message); return new(); }

        using (HttpClient client = new HttpClient())
        {
            var url = $"{MWBase}/v2/chains/1X7UTqDJ/orders/ids?status=accepted&pastNumberOfHours=3";
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("problem is " + response.StatusCode);
            }

            var result = await response.Content.ReadAsStringAsync();
            var ids = JsonSerializer.Deserialize<OrderIdentifiersResponse>(result,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            this.OrderIdentifiers = ids?.Orders ?? new();

            return await GetOrderDetails();
        }
    }

    public async Task<List<YemeksepetiOrderModel>> GetOrderDetails()
    {
        string bearer;
        try { bearer = await GetBearerAsync(); } catch (Exception ex) { Console.WriteLine(ex.Message); return new(); }

        var list = new List<YemeksepetiOrderModel>();
        if (OrderIdentifiers == null || OrderIdentifiers.Count == 0) return list;

        foreach (var orderId in OrderIdentifiers)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            var url = $"{MWBase}/v2/chains/1X7UTqDJ/orders/{orderId}";
            var resp = await client.GetAsync(url);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine("problem " + resp.StatusCode);
                continue;
            }

            try
            {
                var rootObject = JsonSerializer.Deserialize<RootObject>(body);
                if (rootObject?.order != null) list.Add(rootObject.order);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
        return list;
    }

    // ======= MODELLER =======

    public class UpdateStatusBody
    {
        // accepted için
        public string? acceptanceTime { get; set; }     // ISO-8601
        public string? remoteOrderId { get; set; }      // opsiyonel
        public Modifications? modifications { get; set; } // opsiyonel

        // rejected için
        public string? reason { get; set; }             // tercih edilen
        public string? message { get; set; }            // opsiyonel

        // eski isimler (geriye uyum)
        public string? rejectionReasonId { get; set; }
        public string? rejectionNote { get; set; }
    }

    public class RootObject { public YemeksepetiOrderModel order { get; set; } }

    public class Modifications { public List<object>? Products { get; set; } }

    public class OrderIdentifiersResponse
    {
        [JsonPropertyName("orders")] public List<string> Orders { get; set; }
        [JsonPropertyName("count")] public int Count { get; set; }
    }

    public class YemeksepetiLoginResponse
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public int expires_in { get; set; }
    }

    // ---- Availability DTO’ları ----
    public class AvailabilityItem
    {
        public string availabilityState { get; set; }          // OPEN | CLOSED | CLOSED_UNTIL | ...
        public List<string>? availabilityStates { get; set; }
        public bool? changeable { get; set; }
        public string? closedReason { get; set; }
        public List<int>? closingMinutes { get; set; }
        public List<string>? closingReasons { get; set; }
        public string? platformId { get; set; }
        public string? platformKey { get; set; }               // örn: YS_TR
        public string? platformRestaurantId { get; set; }      // bazı platformlarda string
        public string? platformType { get; set; }
    }

    public class SetAvailabilityBody
    {
        public string chainCode { get; set; }
        public string posVendorId { get; set; }
        public string availabilityState { get; set; }          // OPEN | CLOSED | CLOSED_UNTIL
        public string? closedReason { get; set; }              // CLOSED/CLOSED_UNTIL için zorunlu
        public int? closingMinutes { get; set; }               // CLOSED_UNTIL için zorunlu
        public string platformKey { get; set; }                // GET’ten gelen
        public string platformRestaurantId { get; set; }       // GET’ten gelen (string ya da sayı olabilir)
    }

    // ---- Order DTO’ları (mevcut) ----
    public class YemeksepetiOrderModel
    {
        public string token { get; set; } = string.Empty;
        public string code { get; set; } = string.Empty;
        public Comments? comments { get; set; }
        public DateTime? createdAt { get; set; }
        public Customer? customer { get; set; }
        public List<Discount>? discounts { get; set; }
        public string expeditionType { get; set; } = string.Empty;
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

        public Delivery? delivery { get; set; }
        public ExtraParameters? extraParameters { get; set; }
        public InvoicingInformation? invoicingInformation { get; set; }
        public string? shortCode { get; set; }
        public Pickup? pickup { get; set; }
        public PreparationTimeAdjustments? preparationTimeAdjustments { get; set; }
    }

    public class Comments { public string? customerComment { get; set; } }
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

    public class Sponsorship { public string? sponsor { get; set; } public string? amount { get; set; } }
    public class ExtraParameters { public Dictionary<string, string>? properties { get; set; } }
    public class InvoicingInformation { public string? carrierType { get; set; } public string? carrierValue { get; set; } }

    public class LocalInfo
    {
        public string? countryCode { get; set; }
        public string? currencySymbol { get; set; }
        public string? platform { get; set; }
        public string? platformKey { get; set; }
    }

    public class Payment { public string? status { get; set; } public string? type { get; set; } }
    public class Pickup { public DateTime? pickupTime { get; set; } public string? pickupCode { get; set; } }
    public class PlatformRestaurant { public string? id { get; set; } }

    public class Price
    {
        public List<DeliveryFee>? deliveryFees { get; set; }
        public string? grandTotal { get; set; }
        public string? totalNet { get; set; }
        public string? vatTotal { get; set; }
        public string? payRestaurant { get; set; }
        public string? riderTip { get; set; }
        public string? collectFromCustomer { get; set; }
    }

    public class DeliveryFee { public string? name { get; set; } public double? value { get; set; } }

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
        public List<Discount>? discounts { get; set; }
    }

    public class Variation { public string? name { get; set; } }

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
        public string? status { get; set; }
        public string? acceptanceTime { get; set; }
        public string? remoteOrderId { get; set; }
        public Modifications? modifications { get; set; }
    }
}
