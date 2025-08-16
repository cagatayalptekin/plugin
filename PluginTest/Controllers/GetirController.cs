using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Reflection.Metadata.Ecma335;

namespace PluginTest.Controllers
{
    [ApiController]
    [Route("api/getir")]
    public class GetirController : ControllerBase
    {
        public static CourierNotificationDto courierNotification = new();
        public string token { get; set; }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CourierNotificationDto> _latest
    = new();

        [HttpPost("auth/login")]
        public async Task<IActionResult> Login()
        {

            using (HttpClient client = new HttpClient())
            {


                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var json = JsonSerializer.Serialize(loginRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/auth/login", content);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<LoginResponse>(responseBody);
                token = result.token;
                return Ok(result);
            }



        }



        [HttpPost("food-orders/active")]
        public async Task<List<FoodOrderResponse>> GetActiveOrders()
        {
            string token;

            // 1. Adım: Login ol, token al
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginContent = new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json");
                var loginResponse = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/auth/login", loginContent);


                var loginResult = JsonSerializer.Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync());
                token = loginResult.token;
            }
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("token", token); // ← Doğru olan bu

                var response = await client.PostAsync(
                    "https://food-external-api-gateway.development.getirapi.com/food-orders/active",
                    new StringContent("", Encoding.UTF8, "application/json") // veya null da olabilir
                );




                var responseBody = await response.Content.ReadAsStringAsync();
                var orders = JsonSerializer.Deserialize<List<FoodOrderResponse>>(responseBody);
                Console.WriteLine(JsonSerializer.Serialize(orders, new JsonSerializerOptions { WriteIndented = true }));

                return orders;
            }
            // 2. Adım: Token ile aktif siparişleri çek

        }


        [HttpGet("order-detail/{foodOrderId}")]
        public async Task<IActionResult> GetOrderById(string foodOrderId)
        {
            string token;

            // 1. Adım: Login olup token al
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginContent = new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json");
                var loginResponse = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/auth/login", loginContent);

                if (!loginResponse.IsSuccessStatusCode)
                    return StatusCode((int)loginResponse.StatusCode, await loginResponse.Content.ReadAsStringAsync());

                var loginResult = JsonSerializer.Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync());
                token = loginResult.token;
            }

            // 2. Adım: Siparişi ID ile çek
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var url = $"https://food-external-api-gateway.development.getirapi.com/food-orders/{foodOrderId}";

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var json = await response.Content.ReadAsStringAsync();

                // response'u olduğu gibi dön (ya da istersen modelle deserialize et)
                return Ok(JsonDocument.Parse(json));
            }
        }

        [HttpGet("food-orders/{foodOrderId}/cancel-options")]
        public async Task<IActionResult> GetCancelOptions(string foodOrderId)
        {
            string token;

            // 1. Adım: Login → token al
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginContent = new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json");
                var loginResponse = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/auth/login", loginContent);

                if (!loginResponse.IsSuccessStatusCode)
                    return StatusCode((int)loginResponse.StatusCode, await loginResponse.Content.ReadAsStringAsync());

                var loginResult = JsonSerializer.Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync());
                token = loginResult.token;
            }

            // 2. Adım: Cancel Options isteği gönder
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var url = $"https://food-external-api-gateway.development.getirapi.com/food-orders/{foodOrderId}/cancel-options";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var json = await response.Content.ReadAsStringAsync();
                var options = JsonSerializer.Deserialize<List<CancelOption>>(json);

                return Ok(options);
            }
        }


        [HttpPost("cancel-order/{foodOrderId}")]
        public async Task<IActionResult> CancelOrder(string foodOrderId)
        {
            string token;

            // 1. Login: token al
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginContent = new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json");
                var loginResponse = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/auth/login", loginContent);

                if (!loginResponse.IsSuccessStatusCode)
                    return StatusCode((int)loginResponse.StatusCode, await loginResponse.Content.ReadAsStringAsync());

                var loginResult = JsonSerializer.Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync());
                token = loginResult.token;
            }

            // 2. Cancel options: (iptal nedenini alalım - bu örnekte ilk nedeni otomatik alıyoruz)
            string cancelReasonId;
            string cancelNote;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var cancelOptionsUrl = $"https://food-external-api-gateway.development.getirapi.com/food-orders/{foodOrderId}/cancel-options";
                var cancelOptionsResponse = await client.GetAsync(cancelOptionsUrl);

                if (!cancelOptionsResponse.IsSuccessStatusCode)
                    return StatusCode((int)cancelOptionsResponse.StatusCode, await cancelOptionsResponse.Content.ReadAsStringAsync());

                var cancelOptionsJson = await cancelOptionsResponse.Content.ReadAsStringAsync();
                var cancelOptions = JsonSerializer.Deserialize<List<CancelOption>>(cancelOptionsJson);

                // örnek: ilk nedeni seçiyoruz
                cancelReasonId = cancelOptions.First().id;
                cancelNote = cancelOptions.First().message;
            }

            // 3. İptal işlemini gönder
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var cancelBody = new CancelOrderRequest
                {
                    cancelNote = cancelNote,
                    cancelReasonId = cancelReasonId,
                    productId = foodOrderId // aslında sipariş ID'si
                };

                var json = JsonSerializer.Serialize(cancelBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var cancelUrl = $"https://food-external-api-gateway.development.getirapi.com/food-orders/{foodOrderId}/cancel";
                var cancelResponse = await client.PostAsync(cancelUrl, content);

                if (!cancelResponse.IsSuccessStatusCode)
                    return StatusCode((int)cancelResponse.StatusCode, await cancelResponse.Content.ReadAsStringAsync());

                var responseBody = await cancelResponse.Content.ReadAsStringAsync();
                return Ok(JsonDocument.Parse(responseBody));
            }
        }



        private async Task<IActionResult> UpdateOrderStatus(string foodOrderId, string actionEndpoint)
        {
            string token;

            // Token al
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginContent = new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json");
                var loginResponse = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/auth/login", loginContent);

                if (!loginResponse.IsSuccessStatusCode)
                    return StatusCode((int)loginResponse.StatusCode, await loginResponse.Content.ReadAsStringAsync());

                var loginResult = JsonSerializer.Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync());
                token = loginResult.token;
            }

            // Status güncelle
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("token", token); // ← Doğru olan bu

                var url = $"https://food-external-api-gateway.development.getirapi.com/food-orders/{foodOrderId}/{actionEndpoint}";
                var response = await client.PostAsync(url, null);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var responseBody = await response.Content.ReadAsStringAsync();
                return Ok(JsonDocument.Parse(responseBody));
            }
        }


        [HttpPost("food-orders/{foodOrderId}/verify")]
        public async Task<IActionResult> VerifyOrder(string foodOrderId)
        {
            return await UpdateOrderStatus(foodOrderId, "verify");
        }

        [HttpPost("food-orders/{foodOrderId}/prepare")]
        public async Task<IActionResult> PrepareOrder(string foodOrderId)
        {
            return await UpdateOrderStatus(foodOrderId, "prepare");
        }

        [HttpPost("food-orders/{foodOrderId}/deliver")]
        public async Task<IActionResult> DeliverOrder(string foodOrderId)
        {
            return await UpdateOrderStatus(foodOrderId, "deliver");
        }
        [HttpPost("food-orders/{foodOrderId}/handover")]
        public async Task<IActionResult> HandoverOrder(string foodOrderId)
        {
            return await UpdateOrderStatus(foodOrderId, "handover");
        }
        [HttpGet("restaurants")]
        public async Task<IActionResult> GetRestaurantInfo()
        {
            string token;

            // 1. Login işlemi: token al
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginContent = new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json");
                var loginResponse = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/auth/login", loginContent);

                if (!loginResponse.IsSuccessStatusCode)
                    return StatusCode((int)loginResponse.StatusCode, await loginResponse.Content.ReadAsStringAsync());

                var loginResult = JsonSerializer.Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync());
                token = loginResult.token;
            }

            // 2. Restoran bilgisi çek
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var response = await client.GetAsync("https://food-external-api-gateway.development.getirapi.com/restaurants");

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var json = await response.Content.ReadAsStringAsync();
                var restaurant = JsonSerializer.Deserialize<RestaurantInfo>(json);

                return Ok(restaurant);
            }
        }
        [HttpPost("restaurants/pos-status")]
        public async Task<IActionResult> GetPosStatus()
        {
            var request = new
            {
                appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
            };

            using (var client = new HttpClient())
            {

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/restaurants/pos-status", content);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var responseBody = await response.Content.ReadAsStringAsync();
                return Ok(JsonDocument.Parse(responseBody));
            }
        }
        [HttpPost("restaurants/pos/status/{status}")]
        public async Task<IActionResult> UpdatePosStatus([FromRoute] int posStatus)
        {
            var request = new
            {
                posStatus = posStatus,
                appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
            };

            using (var client = new HttpClient())
            {

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await client.PutAsync("https://food-external-api-gateway.development.getirapi.com/restaurants/pos-status", content);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var responseBody = await response.Content.ReadAsStringAsync();
                return Ok(JsonDocument.Parse(responseBody));
            }
        }



        [HttpGet("restaurants/menu")]
        public async Task<ActionResult<RestaurantMenuResponse>> GetRestaurantMenu()
        {
            string token;

            // 1. Token al
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginContent = new StringContent(
                    JsonSerializer.Serialize(loginRequest),
                    Encoding.UTF8,
                    "application/json"
                );

                var loginResponse = await client.PostAsync(
                    "https://food-external-api-gateway.development.getirapi.com/auth/login",
                    loginContent
                );

                if (!loginResponse.IsSuccessStatusCode)
                    return StatusCode((int)loginResponse.StatusCode, await loginResponse.Content.ReadAsStringAsync());

                var loginResult = JsonSerializer.Deserialize<LoginResponse>(
                    await loginResponse.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                token = loginResult?.token ?? string.Empty;
            }

            if (string.IsNullOrEmpty(token))
                return Unauthorized("Token alınamadı.");

            // 2. Menü verisini çek
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("token", token); // Bearer değil

                var response = await client.GetAsync("https://food-external-api-gateway.development.getirapi.com/restaurants/menu");

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var json = await response.Content.ReadAsStringAsync();

                // ✅ Deserialize et
                var menu = JsonSerializer.Deserialize<RestaurantMenuResponse>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (menu == null)
                    return Problem("Menü deserialize edilemedi.");

                // ✅ Direkt model döndür
                return menu;
            }
        }


        [HttpGet("courier/latest/{orderId}")]
        public ActionResult<CourierNotificationDto> GetLatest(string orderId)
        {
            if (_latest.TryGetValue(orderId, out var dto))
            {
                return dto;
            }
               
            else
            {
                Console.WriteLine(  "bulunamadıı");
                return NotFound($"No notification found for order ID: {orderId}");
            }
        }

        [HttpPost("courier")]
        public IActionResult CourierNotification([FromBody] CourierNotificationDto payload)
        {
            Console.WriteLine("=== Courier Notification Payload ===");
            string json = System.Text.Json.JsonSerializer.Serialize(payload,
        new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true // okunabilir format
        });

           
            Console.WriteLine(json);
            Console.WriteLine();
            if (payload == null)
                Console.WriteLine("payload empty");

        
            // Header yoksa isterseniz zorunlu kılabilirsiniz:
            // else return Unauthorized("Missing X-Api-Key.");

            // Tarihleri parse etmeye çalışalım (ISO-8601 bekleniyor)
            if (!DateTimeOffset.TryParse(payload.CalculationDate, out var calculation))
                Console.WriteLine("calculationDate is not a valid ISO-8601 datetime.");  

            DateTimeOffset? pickupMin = null, pickupMax = null;
            if (payload.Pickup is not null)
            {
                if (!string.IsNullOrWhiteSpace(payload.Pickup.Min) &&
                    DateTimeOffset.TryParse(payload.Pickup.Min, out var minVal))
                {
                    pickupMin = minVal;
                }
                if (!string.IsNullOrWhiteSpace(payload.Pickup.Max) &&
                    DateTimeOffset.TryParse(payload.Pickup.Max, out var maxVal))
                {
                    pickupMax = maxVal;
                }
            }

            // Burada: DB’ye yazabilir, in-memory cache güncelleyebilir, loglayabilirsiniz
            Console.WriteLine($"[CourierNotify] order={payload.OrderId} rest={payload.RestaurantId} " +
                              $"calc={calculation:o} pickupMin={(pickupMin?.ToString("o") ?? "-")} " +
                              $"pickupMax={(pickupMax?.ToString("o") ?? "-")}");

            // Örn. domain event / message queue tetiklemek isterseniz burada yapın

            // Getir tarafına 200 OK dönmek yeterli
            _latest[payload.OrderId] = payload;
            return Ok();
        }
    }

    // === DTO'lar ===
    public class CourierNotificationDto
    {
        [JsonPropertyName("orderId")]
        public string? OrderId { get; set; }

        [JsonPropertyName("restaurantId")]
        public string? RestaurantId { get; set; }

        // ISO-8601 string (örn: 2025-08-14T12:34:56+03:00)
        [JsonPropertyName("calculationDate")]
        public string? CalculationDate { get; set; }

        [JsonPropertyName("pickup")]
        public CourierPickupWindow? Pickup { get; set; }
    }

    public class CourierPickupWindow
    {
        // ISO-8601 string
        [JsonPropertyName("min")]
        public string? Min { get; set; }

        // ISO-8601 string
        [JsonPropertyName("max")]
        public string? Max { get; set; }
    }










    //private const string ApiKey = "X7kL93-fgh8W-Zmq0P-Ak2N9";

    //private bool IsAuthorized()
    //{
    //    return Request.Headers.TryGetValue("x-api-key", out var apiKey) && apiKey == ApiKey;
    //}

    //// 1. Yeni Sipariş Webhook (birden fazla sipariş olabilir)
    //[HttpPost("newOrder")]
    //public IActionResult NewOrder([FromBody] List<GetirOrderModel> orders)
    //{
    //    if (!IsAuthorized()) return Unauthorized();

    //    // Siparişleri işle
    //    return Ok(new { status = "orders received", count = orders.Count });
    //}

    //// 2. Sipariş İptali Webhook
    //[HttpPost("cancelOrder")]
    //public IActionResult CancelOrder([FromBody] CancelRequest cancel)
    //{
    //    if (!IsAuthorized()) return Unauthorized();

    //    return Ok(new { status = "cancel received", cancel.OrderId });
    //}

    //// 3. Kurye Restorana Ulaştı Webhook
    //[HttpPost("courierArrived")]
    //public IActionResult CourierArrived([FromBody] CourierArrivalModel courier)
    //{
    //    if (!IsAuthorized()) return Unauthorized();

    //    return Ok(new { status = "courier arrived", courier.OrderId, courier.ArrivalTime });
    //}

    //// 4. Restoran Statü Değişikliği Bildirimi Webhook
    //[HttpPost("statusChange")]
    //public IActionResult StatusChange([FromBody] PosStatusChangeModel statusChange)
    //{
    //    if (!IsAuthorized()) return Unauthorized();

    //    return Ok(new { status = "restaurant status received", restaurant = statusChange.RestaurantSecretKey });
    //}


 

public sealed class RestaurantMenuResponse
    {
        [JsonPropertyName("productCategories")]
        public List<ProductCategory> ProductCategories { get; set; } = new();
    }

    public sealed class ProductCategory
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public LocalizedText? Name { get; set; }

        [JsonPropertyName("restaurant")]
        public string? Restaurant { get; set; }

        [JsonPropertyName("products")]
        public List<GetirProduct> Products { get; set; } = new();

        [JsonPropertyName("isApproved")]
        public bool IsApproved { get; set; }

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("chainProductCategory")]
        public string? ChainProductCategory { get; set; }
    }

    public sealed class GetirProduct
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("restaurant")]
        public string? Restaurant { get; set; }

        [JsonPropertyName("productCategory")]
        public string? ProductCategory { get; set; }

        [JsonPropertyName("optionCategories")]
        public List<OptionCategory> OptionCategories { get; set; } = new();

        [JsonPropertyName("name")]
        public LocalizedText? Name { get; set; }

        [JsonPropertyName("description")]
        public LocalizedText? Description { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("struckPrice")]
        public decimal StruckPrice { get; set; }

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("isApproved")]
        public bool IsApproved { get; set; }

        [JsonPropertyName("imageURL")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("wideImageURL")]
        public string? WideImageUrl { get; set; }

        [JsonPropertyName("chainProduct")]
        public string? ChainProduct { get; set; }
    }

    public sealed class OptionCategory
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public LocalizedText? Name { get; set; }

        [JsonPropertyName("minCount")]
        public int MinCount { get; set; }

        [JsonPropertyName("maxCount")]
        public int MaxCount { get; set; }

        [JsonPropertyName("removeToppings")]
        public bool RemoveToppings { get; set; }

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("chainOptionCategory")]
        public string? ChainOptionCategory { get; set; }

        [JsonPropertyName("options")]
        public List<Option> Options { get; set; } = new();
    }

    public sealed class Option
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("product")]
        public string? Product { get; set; }

        [JsonPropertyName("chainProduct")]
        public string? ChainProduct { get; set; }

        [JsonPropertyName("name")]
        public LocalizedText? Name { get; set; }

        [JsonPropertyName("clientDisplayName")]
        public LocalizedText? ClientDisplayName { get; set; }

        [JsonPropertyName("optionProduct")]
        public string? OptionProduct { get; set; }

        [JsonPropertyName("chainOptionProduct")]
        public string? ChainOptionProduct { get; set; }

        // API burada string[] döndürüyor
        [JsonPropertyName("optionCategories")]
        public List<string> OptionCategories { get; set; } = new();

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("status")]
        public int Status { get; set; }

        [JsonPropertyName("chainOption")]
        public string? ChainOption { get; set; }
    }

    public sealed class LocalizedText
    {
        [JsonPropertyName("tr")]
        public string? Tr { get; set; }

        [JsonPropertyName("en")]
        public string? En { get; set; }
    }


    public class RestaurantInfo
    {
        public string id { get; set; }
        public int averagePreparationTime { get; set; }
        public int status { get; set; } // 1 = açık, 0 = kapalı
        public bool isCourierAvailable { get; set; }
        public string name { get; set; }
        public bool isStatusChangedByUser { get; set; }
        public int closedSource { get; set; }
    }
    public class CancelOrderRequest
    {
        public string cancelNote { get; set; }
        public string cancelReasonId { get; set; }
        public string productId { get; set; } // dikkat: aslında sipariş ID'si
    }
    public class CancelOption
    {
        public string id { get; set; }
        public string message { get; set; }
    }
    public class FoodOrderResponse
    {
        public string id { get; set; }
        public int status { get; set; }
        public bool isScheduled { get; set; }
        public string confirmationId { get; set; }
        public ClientInfo client { get; set; }
        public CourierInfo courier { get; set; }
        public List<FoodProduct> products { get; set; }
        public string clientNote { get; set; }
        public decimal totalPrice { get; set; }
        public decimal totalDiscountedPrice { get; set; }
        public string checkoutDate { get; set; }
        public int deliveryType { get; set; }
        public bool doNotKnock { get; set; }
        public bool isEcoFriendly { get; set; }
        public RestaurantInfo restaurant { get; set; }
        public int paymentMethod { get; set; }
        public PaymentMethodText paymentMethodText { get; set; }
        public bool isQueued { get; set; }
    }

    public class ClientInfo
    {
        public string id { get; set; }
        public string name { get; set; }
        public Location location { get; set; }
        public string clientPhoneNumber { get; set; }
        public string contactPhoneNumber { get; set; }
        public DeliveryAddress deliveryAddress { get; set; }
    }

    public class CourierInfo
    {
        public string id { get; set; }
        public int status { get; set; }
        public string name { get; set; }
        public Location location { get; set; }
    }

    public class FoodProduct
    {
        public string id { get; set; }
        public int count { get; set; }
        public string product { get; set; }
        public ProductName name { get; set; } // <-- string değil, obje!
        public decimal totalPriceWithOption { get; set; }
        public decimal totalPrice { get; set; }
    }

    public class ProductName
    {
        public string tr { get; set; }
        public string en { get; set; }
    }

    public class Location
    {
        public double lat { get; set; }
        public double lon { get; set; }
    }

    public class DeliveryAddress
    {
        public string id { get; set; }
        public string address { get; set; }
        public string aptNo { get; set; }
        public string floor { get; set; }
        public string doorNo { get; set; }
        public string city { get; set; }
        public string district { get; set; }
        public string description { get; set; }
    }



    public class PaymentMethodText
    {
        public string tr { get; set; }
        public string en { get; set; }
    }

    public class LoginRequest
    {
        public string appSecretKey { get; set; }
        public string restaurantSecretKey { get; set; }
    }
    public class LoginResponse
    {
        public string token { get; set; }
    }

}