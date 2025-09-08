using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Reflection.Metadata.Ecma335;
using Microsoft.AspNetCore.Identity.Data;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection.Metadata;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestPDF.Drawing;
using System.IO.Compression;
using System.Net.Http.Headers;


namespace PluginTest.Controllers
{
    [ApiController]
    [Route("api/getir")]
    public class GetirController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;

        private readonly OrderStream _orderStream;
        public GetirController(OrderStream orderStream, IConfiguration cfg, IWebHostEnvironment env)
        {
            _orderStream = orderStream;
            // appsettings.json:  "Getir": { "XApiKey": "..." }
            // veya ENV: GETIR_X_API_KEY
           
            _env = env;
        }

        public static CourierNotificationDto courierNotification = new();
        public string token { get; set; }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CourierNotificationDto> _latest
    = new();


        private const string BaseUrl = "https://food-external-api-gateway.development.getirapi.com";
        private const string AppSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d";
        private const string RestaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b";
        
        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private async Task<string> GetTokenAsync()
        {
            using var client = new HttpClient();
            var loginPayload = new { appSecretKey = AppSecretKey, restaurantSecretKey = RestaurantSecretKey };
            var content = new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json");

            var resp = await client.PostAsync($"{BaseUrl}/auth/login", content);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync();
            var parsed = JsonSerializer.Deserialize<TokenResponse>(json, JsonOpts);
            return parsed?.Token ?? throw new InvalidOperationException("Token alınamadı.");
        }

        // POST api/getir/restaurants/courier/enable
        [HttpPost("restaurants/courier/enable")]
        public async Task<IActionResult> EnableCourier()
        {
            var token = await GetTokenAsync();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("token", token);

            var resp = await client.PostAsync($"{BaseUrl}/restaurants/courier/enable",
                                              new StringContent("{}", Encoding.UTF8, "application/json"));
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

            return Content(body, "application/json");
        }

        [HttpGet("orders/stream")]
        public async Task Stream(CancellationToken ct)
        {
            Response.Headers.Add("Content-Type", "text/event-stream");
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");
            Response.Headers.Add("X-Accel-Buffering", "no"); // nginx/proxy buffer kapat

            // İlk keepalive
            await Response.WriteAsync($": connected {DateTime.UtcNow:o}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            var reader = _orderStream.Reader;

            while (!ct.IsCancellationRequested)
            {
                // Kanalda veri bekle + 15sn'de bir keepalive gönder
                var waitTask = reader.WaitToReadAsync(ct).AsTask();
                var delayTask = Task.Delay(TimeSpan.FromSeconds(15), ct);
                var completed = await Task.WhenAny(waitTask, delayTask);

                if (completed == waitTask && waitTask.Result)
                {
                    while (reader.TryRead(out var msg))
                    {
                        await Response.WriteAsync($"event: new-order\n", ct);
                        await Response.WriteAsync($"data: {msg}\n\n", ct);
                        await Response.Body.FlushAsync(ct);
                    }
                }
                else
                {
                    // keepalive yorumu (SSE yorum satırı)
                    await Response.WriteAsync($": keepalive {DateTime.UtcNow:o}\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
        }

        [HttpPost("newOrder")]
        public IActionResult NewOrder([FromBody] FoodOrderResponse body)
        {
            Console.WriteLine("✅ Getir → NewOrder webhook tetiklendi");

            // Ham JSON’u pretty-print et
            var json = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);

            // Gövde boşsa hata dön
            
           

            // SSE’ye minimal payload gönder
            var payload = JsonSerializer.Serialize(new
            {
                source = "Getir",
                kind = "new",
                 code=body.confirmationId,
                confirmationId =body.confirmationId,
                total =  body.totalPrice,
                     
                at = DateTime.UtcNow
            });

            _ = _orderStream.PublishAsync(payload); // SSE kanalına gönder

            return Ok(new { ok = true });
        }


        // ========== CANCEL ORDER ==========
        [HttpPost("cancelOrder")]
        
        public IActionResult CancelOrder([FromBody] FoodOrderResponse body)
        {
             
            if (body is null || string.IsNullOrWhiteSpace(body.id))
                return BadRequest("Body veya id eksik.");

            var reasonTr = body.cancelReason?.messages?.Tr;
            var reasonEn = body.cancelReason?.messages?.En;

            var payload = JsonSerializer.Serialize(new
            {
                source = "Getir",
                kind = "cancel",
                
                code = body.confirmationId,
                reason = (string?)reasonTr ?? body.cancelNote ?? (string?)reasonEn,
                at = DateTime.UtcNow
            });

            _ = _orderStream.PublishAsync(payload); // SSE kanalına gönder

            return Ok(new { ok = true });
        }



















        public class DisableCourierDto { public int? TimeOffAmount { get; set; } } // dk cinsinden

        // POST api/getir/restaurants/courier/disable
        [HttpPost("restaurants/courier/disable")]
        public async Task<IActionResult> DisableCourier([FromBody] DisableCourierDto dto)
        {
            var token = await GetTokenAsync();

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("token", token);
            var request = new
            {

                timeOffAmount = dto.TimeOffAmount
            };
            var json = JsonSerializer.Serialize(request, JsonOpts);
            // minutes verilmediyse boş obje gönder (süresiz)
          
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var resp = await client.PostAsync($"{BaseUrl}/restaurants/courier/disable", content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

            return Content(body, "application/json");
        }

       



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
                if (orders?.Count > 0)
                {
                    await GetOrderById(orders.FirstOrDefault().id);
                }
         
           
                return orders;
            }
            // 2. Adım: Token ile aktif siparişleri çek

        }
        [HttpPost("food-orders/periodic/unapproved")]
        public async Task<List<FoodOrderResponse>> GetUnapprovedOrders()
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
                    "https://food-external-api-gateway.development.getirapi.com/food-orders/periodic/unapproved",
                    new StringContent("", Encoding.UTF8, "application/json") // veya null da olabilir
                );




                var responseBody = await response.Content.ReadAsStringAsync();
                var orders = JsonSerializer.Deserialize<List<FoodOrderResponse>>(responseBody);
                Console.WriteLine(JsonSerializer.Serialize(orders, new JsonSerializerOptions { WriteIndented = true }));
                if (orders?.Count > 0)
                {
                    await GetOrderById(orders.FirstOrDefault().id);
                }


                return orders;
            }
            // 2. Adım: Token ile aktif siparişleri çek

        }
        [HttpPost("food-orders/periodic/cancelled")]
        public async Task<List<FoodOrderResponse>> GetCancelledOrders()
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
                    "https://food-external-api-gateway.development.getirapi.com/food-orders/periodic/cancelled",
                    new StringContent("", Encoding.UTF8, "application/json") // veya null da olabilir
                );




                var responseBody = await response.Content.ReadAsStringAsync();
                var orders = JsonSerializer.Deserialize<List<FoodOrderResponse>>(responseBody);
                Console.WriteLine(JsonSerializer.Serialize(orders, new JsonSerializerOptions { WriteIndented = true }));
                if (orders?.Count > 0)
                {
                    await GetOrderById(orders.FirstOrDefault().id);
                }


                return orders;
            }
            // 2. Adım: Token ile aktif siparişleri çek

        }
        [HttpGet("food-orders/history-csv")]
        [Produces("text/csv")]
        public async Task<IActionResult> DownloadOrdersCsv(
     [FromQuery] string startDate,
     [FromQuery] string endDate,
     [FromQuery] string restaurantIds,   // tek id de olabilir
     [FromQuery] string? templateName
 )
        {
            // 1) Login
            string token;
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginResp = await client.PostAsync(
                    "https://food-external-api-gateway.development.getirapi.com/auth/login",
                    new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json")
                );
                var loginBody = await loginResp.Content.ReadAsStringAsync();
                if (!loginResp.IsSuccessStatusCode)
                    return StatusCode((int)loginResp.StatusCode, loginBody);

                var login = JsonSerializer.Deserialize<LoginResponse>(loginBody);
                token = login?.token ?? throw new Exception("Token alınamadı.");
            }

            // 2) Query string
            var qs = new List<string>
    {
        $"startDate={Uri.EscapeDataString(startDate)}",
        $"endDate={Uri.EscapeDataString(endDate)}",
        $"restaurantIds={Uri.EscapeDataString(restaurantIds)}"
    };
            if (!string.IsNullOrWhiteSpace(templateName))
                qs.Add($"templateName={Uri.EscapeDataString(templateName)}");

            var url = "https://food-external-api-gateway.development.getirapi.com/food-orders/report/details?" + string.Join("&", qs);

            // 3) İstek
            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("token", token);
            // JSON veya CSV gelebilir — ikisini de kabul edelim
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            var fileName = $"orders-{startDate}-{endDate}.csv";

            if (!resp.IsSuccessStatusCode)
            {
                var errText = await resp.Content.ReadAsStringAsync();
                return StatusCode((int)resp.StatusCode, errText);
            }

            var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
            // JSON geldiyse -> CSV’ye çevir
            if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = await resp.Content.ReadAsStringAsync();
                var csv = JsonArrayToCsv(json);
                // UTF-8 BOM’lu döndür (Excel için iyi olur)
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
                return File(bytes, "text/csv", fileName);
            }

            // Doğrudan CSV/binary geldiyse aynen geçir
            var raw = await resp.Content.ReadAsByteArrayAsync();
            var ct = string.IsNullOrWhiteSpace(mediaType) ? "text/csv" : mediaType;
            return File(raw, ct, fileName);
        }

        // Basit ve güvenli CSV dönüştürücü: başlıkları sabit + varsa ekstra alanları sona ekler
        private static string JsonArrayToCsv(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "";

            // Beklenen sütun sırası (örnek JSON’dan)
            var preferred = new List<string>
    {
        "city","paymentMethod","deliveryType","companyName","taxNo","iban",
        "restaurantId","branchName","restaurantName","foodOrderId","date",
        "finalPrice","finalPriceTaxExcluded","supplierSupport","chargedAmountTaxExcluded",
        "supplierProductRevenue","supplierProductRevenueTaxExcluded","damagedProductTotal",
        "commissionBasketValue","commissionBasketValueTaxExcluded","damagedProductSource",
        "netCommissionRevenue","supplierNetRevenue","loyaltyFee","totalDiscountedPrice",
        "visibilityFeeAmount","withholdingTaxAmount","deferredPaymentDate",
        "paymentStatus","orderStatus","partialRefundsPrice"
    };

            // Ekstra alanlar varsa yakala
            var extras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                foreach (var p in row.EnumerateObject())
                {
                    if (!preferred.Contains(p.Name) && !extras.Contains(p.Name))
                        extras.Add(p.Name);
                }
            }

            var headers = preferred.Concat(extras).ToList();
            var sb = new StringBuilder();

            // Header
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

            // Rows
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var cells = new List<string>(headers.Count);
                foreach (var h in headers)
                {
                    string val = "";
                    if (row.TryGetProperty(h, out var el))
                    {
                        val = el.ValueKind switch
                        {
                            JsonValueKind.String => el.GetString() ?? "",
                            JsonValueKind.Number => el.ToString(), // kültürden bağımsız nokta
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            JsonValueKind.Null => "",
                            _ => el.ToString()    // obje/array için JSON string
                        };
                    }
                    cells.Add(EscapeCsv(val));
                }
                sb.AppendLine(string.Join(",", cells));
            }

            return sb.ToString();
        }

        private static string EscapeCsv(string? s)
        {
            s ??= "";
            // virgül, çift tırnak, satır sonu varsa -> "..." ve iç tırnakları çiftle
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }
        [HttpGet("food-orders/history-rows")]
        [Produces("application/json")]
        public async Task<IActionResult> GetHistoryRows(
    [FromQuery] string startDate,
    [FromQuery] string endDate,
    [FromQuery] string restaurantIds,   // tek id de olabilir
    [FromQuery] string? templateName
)
        {
            // (Opsiyonel ama faydalı) 7 gün guard
            if (DateTime.TryParse(startDate, out var s) &&
                DateTime.TryParse(endDate, out var e) &&
                (e - s).TotalDays > 7)
            {
                return BadRequest(new { code = 13, error = "FinancialReportMaxDaysError", message = "maximum day limit is 7 days" });
            }

            // 1) Login
            string token;
            using (var client = new HttpClient())
            {
                var loginRequest = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginResp = await client.PostAsync(
                    "https://food-external-api-gateway.development.getirapi.com/auth/login",
                    new StringContent(JsonSerializer.Serialize(loginRequest), Encoding.UTF8, "application/json")
                );

                var loginBody = await loginResp.Content.ReadAsStringAsync();
                if (!loginResp.IsSuccessStatusCode)
                    return StatusCode((int)loginResp.StatusCode, loginBody);

                var login = JsonSerializer.Deserialize<LoginResponse>(loginBody);
                token = login?.token ?? throw new Exception("Token alınamadı.");
            }

            // 2) Query string
            var qs = new List<string>
    {
        $"startDate={Uri.EscapeDataString(startDate)}",
        $"endDate={Uri.EscapeDataString(endDate)}",
        $"restaurantIds={Uri.EscapeDataString(restaurantIds)}"
    };  
            templateName = "RestaurantFinancial";
            if (!string.IsNullOrWhiteSpace(templateName))
                qs.Add($"templateName={Uri.EscapeDataString(templateName)}");
            
            var url = "https://food-external-api-gateway.development.getirapi.com/food-orders/report/details?" + string.Join("&", qs);

            // 3) İstek (JSON isteyelim)
            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("token", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, body);

            // Upstream JSON array’ı olduğu gibi geçir
            return Content(body, "application/json; charset=utf-8");
        }

        [HttpGet("order-detail/{foodOrderId}")]
        public async Task<IActionResult> GetOrderById(string foodOrderId)
        {
            string token;
            Console.WriteLine("food id is that"+foodOrderId);
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
                client.DefaultRequestHeaders.Add("token", token);

                var url = $"https://food-external-api-gateway.development.getirapi.com/food-orders/{foodOrderId}";

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var json = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Food order is"+json);
                // response'u olduğu gibi dön (ya da istersen modelle deserialize et)
                return Ok(JsonDocument.Parse(json));
            }
        }

        [HttpGet("food-orders/{foodOrderId}/cancel-options")]
        public async Task<IActionResult> GetCancelOptions([FromRoute]string foodOrderId)
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
                client.DefaultRequestHeaders.Add("token", token);

                var url = $"https://food-external-api-gateway.development.getirapi.com/food-orders/{foodOrderId}/cancel-options";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var json = await response.Content.ReadAsStringAsync();
                var options = JsonSerializer.Deserialize<List<CancelOption>>(json);

                return Ok(options);
            }
        }


        [HttpPost("cancel-order/{foodOrderId}/{cancelNote}/{cancelReasonId}")]
        public async Task<IActionResult> CancelOrder([FromRoute]string foodOrderId, [FromRoute] string cancelNote, [FromRoute] string cancelReasonId)
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

                 

        

            // 3. İptal işlemini gönder
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("token", token);

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


        [HttpPost("food-orders/{foodOrderId}/verify/{status}")]
        public async Task<IActionResult> VerifyOrder([FromRoute] string foodOrderId, [FromRoute] int status)
        {
            if(status==400)
            {
                return await UpdateOrderStatus(foodOrderId, "verify");
            }
            else if(status==325)
            {
                return await UpdateOrderStatus(foodOrderId, "verify-scheduled");
            }
            return Ok(foodOrderId);
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
         
        public async Task<IActionResult> GetRestaurantInfoNotUsed()
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





        [HttpGet("products/{productId}/status")]
        public async Task<IActionResult> GetProductStatus([FromRoute] string productId)
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

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("token", token); // Getir expects 'token' header here

                var resp = await client.GetAsync($"https://food-external-api-gateway.development.getirapi.com/products/{productId}/status");
                var body = await resp.Content.ReadAsStringAsync();

                Console.WriteLine($"[GET /products/{productId}/status] {resp.StatusCode}");
                Console.WriteLine(body);

                if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

                // Pass-through typed or raw:
                var model = JsonSerializer.Deserialize<ProductStatusResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return Ok(model);
            }
            return Ok();
        }

        [HttpPut("products/{productId}/status")]
        public async Task<IActionResult> UpdateProductStatus([FromRoute] string productId, [FromBody] UpdateProductStatusRequest req)
        {



            if (req == null || (req.status != 100 && req.status != 200 && req.status != 400))
                return BadRequest("status must be 100 (ACTIVE), 200 (INACTIVE) or 400 (DAILY_INACTIVE).");

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
            using (var client = new HttpClient())
            {
                Console.WriteLine(  "MY PRODUCT ID is that",productId);

                client.DefaultRequestHeaders.Add("token", token);

                var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");

                var resp = await client.PutAsync($"https://food-external-api-gateway.development.getirapi.com/products/{productId}/status", content);
                var body = await resp.Content.ReadAsStringAsync();

                Console.WriteLine($"[PUT /products/{productId}/status] {resp.StatusCode}");
                Console.WriteLine(body);

                if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

                // Return updated status payload if provided; if empty, return { status: req.status } as confirmation
                if (string.IsNullOrWhiteSpace(body))
                    return Ok(new { id = productId, status = req.status });
                return Ok(JsonDocument.Parse(body));
            }
            return Ok();
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
        public async Task<IActionResult> UpdatePosStatus([FromRoute] int status)
        {
            var request = new
            {
                posStatus = status,
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
                Console.WriteLine("✅ Response status: " + response.StatusCode);
                Console.WriteLine("✅ Response body: " + responseBody);
                return Ok(JsonDocument.Parse(responseBody));
            }
        }
        [HttpGet("food-orders/{foodOrderId}/receipt.png")]
        public async Task<IActionResult> GetOrderReceiptPdf(string foodOrderId)
        {
            // 1) Login → token
            string token;
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

                var loginResult = JsonSerializer.Deserialize<LoginResponse>(await loginResponse.Content.ReadAsStringAsync(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                token = loginResult?.token ?? "";
            }

            // 2) Siparişi çek
            FoodOrderResponse order;
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("token", token);
                var url = $"https://food-external-api-gateway.development.getirapi.com/food-orders/{foodOrderId}";
                var resp = await client.GetAsync(url);
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

                order = JsonSerializer.Deserialize<FoodOrderResponse>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            }

            // 3) Logo bytes (opsiyonel)
            byte[]? logoBytes = null;
            try
            {
                var logoPath = Path.Combine(_env.WebRootPath, "uploads", "getir-logo.png");

                if (System.IO.File.Exists(logoPath))
                    logoBytes = await System.IO.File.ReadAllBytesAsync(logoPath);
            }
            catch { /* logla istersen */ }

            // 4) PDF oluştur
            var png = ReceiptImageBuilder.BuildPng(order, logoBytes);
            return File(png, "image/png", $"siparis_fisi_{order.id}.png");
        }
        public static class ReceiptImageBuilder
        {
            public static byte[] BuildPng(FoodOrderResponse o, byte[]? logoBytes)
            {
                // Küçük yardımcılar
                var tr = CultureInfo.GetCultureInfo("tr-TR");
                string Money(decimal v) => string.Format(tr, "{0:N2} ₺", v);
                string TryDate(string? iso) => DateTime.TryParse(iso, out var dt)
                    ? dt.ToString("dd.MM.yyyy HH:mm", tr)
                    : "-";

                // Telefon (modelinde olan alanlar)
                string phone =
                    o?.client?.clientPhoneNumber
                    ?? o?.client?.contactPhoneNumber
                    ?? "-";

                // Ödeme
                string payment =
                    o?.paymentMethodText?.tr
                    ?? o?.paymentMethodText?.en
                    ?? (o?.paymentMethod > 0 ? $"Ödeme Yöntemi: {o.paymentMethod}" : "-");

                // Teslimat tipi
                string deliveryType = o?.deliveryType == 2 ? "Restoran Kuryesi" : "Getir Kuryesi:"+o.courier.name.ToString(); 

                // İndirim bilgisi
                var discountedTotal = (o?.totalDiscountedPrice ?? 0m) > 0 ? o!.totalDiscountedPrice : o!.totalPrice;
var discount = Math.Max(0m, (o.totalPrice ?? 0m) - (discountedTotal ?? 0m));
                bool hasDiscount = discount > 0;
                var campaignName = hasDiscount ? "Kampanyalı Sipariş" : null;

                var items = o?.products ?? new List<FoodProduct>();

               
                try
                {
                    var doc = QuestPDF.Fluent.Document.Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Margin(30);
                            page.Size(PageSizes.A4);

                            // HEADER
                            page.Header().Element(header =>
                            {
                                header.Row(row =>
                                {
                                    row.RelativeItem().Column(col =>
                                    {
                                        col.Item().Text("Sipariş Fişi").SemiBold().FontSize(18);
                                        col.Item().Text($"Sipariş ID: {o?.id}").FontSize(10).Light();
                                        col.Item().Text($"Doğrulama Kodu: {o?.confirmationId ?? "-"}").FontSize(10).Light();
                                    });

                                    if (logoBytes is { Length: > 0 })
                                        row.ConstantItem(100).AlignRight().Height(40).Image(logoBytes);
                                });
                            });

                            // CONTENT
                            page.Content().PaddingVertical(10).Column(col =>
                            {
                                page.Background().Background(Colors.White);
                                // Sipariş Detayı
                                col.Item().Text("Sipariş Detayı").SemiBold().FontSize(12);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(2);
                                        c.RelativeColumn(5);
                                    });

                                    void R(string l, string r)
                                    {
                                        t.Cell().PaddingVertical(2).Text(l).FontSize(10).Light();
                                        t.Cell().PaddingVertical(2).Text(r).FontSize(10);
                                    }

                                    R("Tarih", TryDate(o?.checkoutDate));
                                    R("Teslimat Tipi", deliveryType);
                                    R("Ödeme", payment);
                                    if (!string.IsNullOrWhiteSpace(o?.clientNote)) R("Sipariş Notu", o!.clientNote);
                                    R("Müşteri", o?.client?.name ?? "-");
                                    R("Telefon", phone);
                                    R("Müşteri Notu", o.clientNote);
                                });

                                // Ayırıcı (border-bottom ile)
                                col.Item()
                                   .PaddingVertical(8)
                                   .BorderBottom(0.5f)
                                   .BorderColor(Colors.Grey.Lighten2);

                                // Ürünler
                                col.Item().Text("Ürünler").SemiBold().FontSize(12);

                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(1);  // Adet
                                        c.RelativeColumn(5);  // Ürün adı
                                        c.RelativeColumn(2);  // Birim
                                        c.RelativeColumn(2);  // Toplam
                                    });

                                    t.Header(h =>
                                    {
                                        h.Cell().Text("Adet").Bold();
                                        h.Cell().Text("Ürün").Bold();
                                        h.Cell().Text("Birim").Bold();
                                        h.Cell().Text("Toplam").Bold();
                                    });

                                    foreach (var p in items)
                                    {
                                        var name = p?.name?.tr ?? p?.name?.en ?? (p?.product ?? "-");
                                        var adet = p?.count ?? 0;
                                        var unitPrice = p?.totalPrice ?? 0m;

                                        // totalPriceWithOption varsa onu kullan, yoksa adet * birim
                                        decimal lineTotal = (p?.totalPriceWithOption ?? 0m) > 0
     ? (p!.totalPriceWithOption ?? 0m)
     : unitPrice * adet;

                                        t.Cell().Text(adet.ToString());
                                        t.Cell().Text(name);
                                        t.Cell().Text(Money(unitPrice));
                                        t.Cell().Text(Money(lineTotal));
                                    }
                                });

                                // Tutarlar
                                col.Item().AlignRight().Column(rcol =>
                                {
                                    rcol.Item().Text($"Ara Toplam: {Money((o!.totalPrice ?? 0m))}").FontSize(10);
                                    if (hasDiscount)
                                        rcol.Item().Text($"İndirim{(campaignName != null ? $" ({campaignName})" : "")}: -{Money(discount)}").FontSize(10);
                                    rcol.Item().Text($"İndirimli Toplam: {Money(discountedTotal??0m)}").FontSize(12).SemiBold();
                                });

                                col.Item().PaddingTop(10)
                                   .Text("Not: Bu fiş Getir test verilerinden üretilmiştir.")
                                   .FontSize(9).Light();
                            });

                            // FOOTER
                            page.Footer().AlignCenter().Text(x =>
                            {
                                x.Span("© ").FontSize(9);
                                x.Span(DateTime.Now.ToString("yyyy"));
                            });
                        });
                    });

                    // PNG çıktı (QuestPDF bu overload’da PNG üretir)
                    var images = doc.GenerateImages();   // istersen doc.GenerateImages(144) (dpi) kullan

                    return images.First();
                }
                catch (Exception e)
                {

                    Console.WriteLine("msj",e.Message);
                }
                return new byte[10];
               
            }

            private static string TryDate(string? iso)
            {
                if (DateTimeOffset.TryParse(iso, out var dt))
                    return dt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                return "-";
            }
        }


        [HttpPost("restaurants/status/{status}/{time}")]
        public async Task<IActionResult> CloseRestaurant([FromRoute] int status, [FromRoute] int time)
        {
            Console.WriteLine("✅ Time  is: " + time);
            Console.WriteLine("✅ status is : " + status);
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


         

            using (var client = new HttpClient())
            {
                var response= new HttpResponseMessage();
                client.DefaultRequestHeaders.Add("token", token); // Bearer değil

                if (status==100)
                {
                     response = await client.PutAsync("https://food-external-api-gateway.development.getirapi.com/restaurants/status/open", null);
                }
                else if (status == 200)
                {
                    var request = new
                    {

                        timeOffAmount = time
                    };
                    var json = JsonSerializer.Serialize(request);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");

                    response = await client.PutAsync("https://food-external-api-gateway.development.getirapi.com/restaurants/status/close", content);
                }
                
               

                if (!response.IsSuccessStatusCode)
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());

                var responseBody = await response.Content.ReadAsStringAsync();
                Console.WriteLine("✅ Response status: " + response.StatusCode);
                Console.WriteLine("✅ Response body: " + responseBody);
                return Ok();
            }
        }
        [HttpGet("restaurants")]
        public async Task<IActionResult> GetRestaurantInfo()
        {
            string token;

            // 1) Login → token
            using (var client = new HttpClient())
            {
                var loginPayload = new
                {
                    appSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d",
                    restaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b"
                };

                var loginContent = new StringContent(JsonSerializer.Serialize(loginPayload), Encoding.UTF8, "application/json");
                var loginResp = await client.PostAsync("https://food-external-api-gateway.development.getirapi.com/auth/login", loginContent);
                var loginBody = await loginResp.Content.ReadAsStringAsync();
                if (!loginResp.IsSuccessStatusCode) return StatusCode((int)loginResp.StatusCode, loginBody);

                var login = JsonSerializer.Deserialize<LoginResponse>(loginBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                token = login?.token ?? "";
            }

            // 2) GET /restaurants
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("token", token); // per docs

                var resp = await client.GetAsync("https://food-external-api-gateway.development.getirapi.com/restaurants");
                var body = await resp.Content.ReadAsStringAsync();

                Console.WriteLine($"[GET /restaurants] {resp.StatusCode}");
                Console.WriteLine(body);

                if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

                var model = JsonSerializer.Deserialize<RestaurantInfoResponse>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (model == null) return StatusCode(502, "Invalid restaurant payload.");

                return Ok(model); // ← Angular receives { id, status, ... }
            }
        }
        [HttpPost("restaurant")]
        public IActionResult StatusChange([FromBody] PosStatusChangeModel statusChange)
        {
            Console.WriteLine("qwe");
            // Gelen payload'u komple JSON olarak console'a yaz
            Console.WriteLine(
                $"RestaurantId: {statusChange.RestaurantId}, " +
                $"RestaurantName: {statusChange.RestaurantName}, " +
                $"Status(raw): {statusChange.Status}, " +
                $"StatusChangeDate: {statusChange.StatusChangeDate}"
            );

            // Tüm modeli JSON serialize edip loglamak için:
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(statusChange));

            return Ok();
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
                Console.WriteLine(JsonSerializer.Serialize(menu, new JsonSerializerOptions { WriteIndented = true }));
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

        // AÇ: PUT /restaurants/status/open
        [HttpPost("restaurants/enable")]
        public async Task<IActionResult> EnableRestaurant()
        {
            var token = await GetTokenAsync();

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            // Boş JSON göndersin (Getir tarafı PUT + body bekliyor)
            using var body = new StringContent("{}", Encoding.UTF8, "application/json");
            var resp = await http.PutAsync("https://food-external-api-gateway.development.getirapi.com/restaurants/status/open", body);

            var respBody = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, respBody);

            // İstersen pass-through yap; ben basit OK döndürüyorum:
            return Ok(new { result = true, status = 100 });
        }

        // KAPAT: PUT /restaurants/status/close   (15/30/45 ya da süresiz)
        [HttpPost("restaurants/disable")]
        public async Task<IActionResult> DisableRestaurant([FromBody] DisableRestaurantDto dto)
        {
            var token = await GetTokenAsync();

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            // minutes varsa { timeOffAmount } gönder, yoksa {} (süresiz)
            string json = (dto?.TimeOffAmount is int m)
                ? JsonSerializer.Serialize(new { timeOffAmount = m })
                : "{}";

            using var body = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PutAsync("https://food-external-api-gateway.development.getirapi.com/restaurants/status/close", body);

            var respBody = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, respBody);

            // Süreli kapama => 300, süresiz => 200
            var status = (dto?.TimeOffAmount is int) ? 300 : 200;
            return Ok(new { result = true, status });
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



    public sealed class PosStatusChangeModel
    {
        public string? RestaurantId { get; set; }
        public string? RestaurantName { get; set; }
        public string? Status { get; set; } // enum yerine string tutuyoruz
        public DateTimeOffset? StatusChangeDate { get; set; }
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
    public sealed class DisableRestaurantDto
    {
        public int? TimeOffAmount { get; set; } // 15 / 30 / 45, null => süresiz
    }

    public sealed class ProductStatusResponse
    {
        public string id { get; set; } = "";
        public int status { get; set; } // 100, 200, 400
    }

    public sealed class UpdateProductStatusRequest
    {
        public int status { get; set; } // 100, 200, 400
    }


    public sealed class RestaurantInfoResponse
    {
        public string id { get; set; } = "";
        public int averagePreparationTime { get; set; }
        public int status { get; set; }                 // 100=open, 200=closed (proxy as-is)
        public bool isCourierAvailable { get; set; }
        public string name { get; set; } = "";
        public bool isStatusChangedByUser { get; set; }
        public int closedSource { get; set; }
    }
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

    public class TokenResponse { public string Token { get; set; } = ""; }
    
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
        public string id { get; set; } = "";                  // id her zaman geliyor gibi; istersen string? yap
        public int? status { get; set; }
        public bool? isScheduled { get; set; }
        public string? confirmationId { get; set; }
        public ClientInfo? client { get; set; }
        public CourierInfo? courier { get; set; }
        public List<FoodProduct>? products { get; set; }
        public string? clientNote { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? totalPrice { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? totalDiscountedPrice { get; set; }

        public string? checkoutDate { get; set; }
        public string? scheduledDate { get; set; }
        public int? deliveryType { get; set; }
        public bool? doNotKnock { get; set; }
        public bool? isEcoFriendly { get; set; }
        public RestaurantInfo? restaurant { get; set; }
        public int? paymentMethod { get; set; }
        public PaymentMethodText? paymentMethodText { get; set; }
        public bool? isQueued { get; set; }
        public string? verifyDate { get; set; }
        public string? scheduleVerifiedDate { get; set; }
        public string? prepareDate { get; set; }
        public string? handoverDate { get; set; }
        public string? reachDate { get; set; }
        public string? deliverDate { get; set; }

        public bool? restaurantPanelOperation { get; set; }
        public BrandInfo? brand { get; set; }
        public string? cancelNote { get; set; }
        public CancelReason? cancelReason { get; set; }

        public int? calculatedCourierToRestaurantETA { get; set; }
    }

    public class ClientInfo
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public Location? location { get; set; }
        public string? clientPhoneNumber { get; set; }
        public string? contactPhoneNumber { get; set; }
        public DeliveryAddress? deliveryAddress { get; set; }
        public string? clientUnmaskedPhoneNumber { get; set; }
    }

    public class CourierInfo
    {
        public string? id { get; set; }
        public int? status { get; set; }
        public string? name { get; set; }
        public Location? location { get; set; }
    }

    public class FoodProduct
    {
        public string? id { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? count { get; set; }

        public string? product { get; set; }
        public ProductName? name { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? totalPriceWithOption { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? totalPrice { get; set; }

        public string? imageURL { get; set; }
        public string? wideImageURL { get; set; }
        public string? chainProduct { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? price { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? optionPrice { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? priceWithOption { get; set; }

        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public decimal? totalOptionPrice { get; set; }

        public List<JsonElement>? optionCategories { get; set; }
        public DisplayInfo? displayInfo { get; set; }
        public string? note { get; set; }
    }

    public class BrandInfo
    {
        public string? id { get; set; }
        public string? name { get; set; }
    }

    public class CancelReason
    {
        public string? id { get; set; }
        public LocalizedText? messages { get; set; } // {tr,en}
    }

    public class DisplayInfo
    {
        public LocalizedText? title { get; set; }    // {tr,en}
        public LocalizedStringList? options { get; set; } // {tr:[...], en:[...]}
    }

    public class LocalizedStringList
    {
        public List<string>? tr { get; set; }
        public List<string>? en { get; set; }
    }

    public class ProductName
    {
        public string? tr { get; set; }
        public string? en { get; set; }
    }

    public class Location
    {
        public double? lat { get; set; }
        public double? lon { get; set; }
    }

    public class DeliveryAddress
    {
        public string? id { get; set; }
        public string? address { get; set; }
        public string? aptNo { get; set; }
        public string? floor { get; set; }
        public string? doorNo { get; set; }
        public string? city { get; set; }
        public string? district { get; set; }
        public string? description { get; set; }
    }

    public class PaymentMethodText
    {
        public string? tr { get; set; }
        public string? en { get; set; }
    }

    public class RestaurantInfo
    {
        public string? id { get; set; }
        public int? averagePreparationTime { get; set; }

        // Getir bazen 0/1/100/200 gibi sayılar gönderebilir, bazen string? emin değilsen:
        [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
        public int? status { get; set; }

        public bool? isCourierAvailable { get; set; }
        public string? name { get; set; }
        public bool? isStatusChangedByUser { get; set; }
        public int? closedSource { get; set; }
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