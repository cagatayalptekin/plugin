using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PluginTest.Infrastructure;
using PluginTest.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PluginTest.Controllers
{
    [ApiController]
    [Route("api/getir")]
    public class GetirController : ControllerBase
    {
        // ====== DI & Config ======
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _http;
        private readonly GetirOptions _opt;
        private readonly OrderStream _orderStream;

        public GetirController(
            IWebHostEnvironment env,
            OrderStream orderStream,
            IOptions<GetirOptions> opt,
            IHttpClientFactory http)
        {
            _env = env;
            _orderStream = orderStream;
            _opt = opt.Value;
            _http = http;
        }

        // ====== Consts & Shared ======
        private const string BaseUrl = "https://food-external-api-gateway.development.getirapi.com";
        private const string AppSecretKey = "5687880695ded1b751fb8bfbc3150a0fd0f0576d";
        private const string RestaurantSecretKey = "6cfbb12f2bd594fe6920163136776d2860cfe46b";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        private static readonly ConcurrentDictionary<string, CourierNotificationDto> _latest = new();

        // Tek bir HttpClient (geocode & OSRM) + UA
        private static readonly HttpClient _httpMap = new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                     System.Net.DecompressionMethods.Deflate
        });

        static GetirController()
        {
            _httpMap.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("pos-web-ui", "1.0"));
        }

        private async Task<string> GetTokenAsync()
        {
            using var client = new HttpClient();
            var payload = new { appSecretKey = AppSecretKey, restaurantSecretKey = RestaurantSecretKey };
            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

            var resp = await client.PostAsync($"{BaseUrl}/auth/login", content);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"Login failed: {(int)resp.StatusCode} - {body}");

            var parsed = JsonSerializer.Deserialize<LoginResponse>(body, JsonOpts);
            return parsed?.token ?? throw new InvalidOperationException("Token alınamadı.");
        }

        // =====================================================================
        // =============  POS / KURYE AÇ-KAPA  (Restaurant & Courier) ==========
        // =====================================================================

        [HttpPost("restaurants/enable")]
        public async Task<IActionResult> EnableRestaurant()
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            using var body = new StringContent("{}", Encoding.UTF8, "application/json");
            var resp = await http.PutAsync($"{BaseUrl}/restaurants/status/open", body);
            var respBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, respBody);
            return Ok(new { result = true, status = 100 });
        }

        public class DisableRestaurantDto { public int? TimeOffAmount { get; set; } }

        [HttpPost("restaurants/disable")]
        public async Task<IActionResult> DisableRestaurant([FromBody] DisableRestaurantDto dto)
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var json = (dto?.TimeOffAmount is int m)
                ? JsonSerializer.Serialize(new { timeOffAmount = m }, JsonOpts)
                : "{}";

            using var body = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await http.PutAsync($"{BaseUrl}/restaurants/status/close", body);
            var respBody = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, respBody);

            var status = (dto?.TimeOffAmount is int) ? 300 : 200;
            return Ok(new { result = true, status });
        }

        public class DisableCourierDto { public int? TimeOffAmount { get; set; } } // dk

        [HttpPost("restaurants/courier/enable")]
        public async Task<IActionResult> EnableCourier()
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.PostAsync($"{BaseUrl}/restaurants/courier/enable",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

            return Content(body, "application/json");
        }

        [HttpPost("restaurants/courier/disable")]
        public async Task<IActionResult> DisableCourier([FromBody] DisableCourierDto dto)
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var req = new { timeOffAmount = dto.TimeOffAmount };
            var json = JsonSerializer.Serialize(req, JsonOpts);

            var resp = await http.PostAsync($"{BaseUrl}/restaurants/courier/disable",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

            return Content(body, "application/json");
        }

        // =====================================================================
        // ======================  ORDER STREAM (BİZİM EVENT)  =================
        // =====================================================================

        [HttpPost("newOrder")]
        public IActionResult NewOrder([FromBody] FoodOrderResponse body)
        {
            var payload = JsonSerializer.Serialize(new
            {
                source = "Getir",
                kind = "new",
                code = body.confirmationId ?? Guid.NewGuid().ToString("N"),
                total = body.totalPrice,
                at = DateTime.UtcNow,
                order=body
            }, JsonOpts);

            _orderStream.Publish(payload);
            Console.WriteLine(JsonSerializer.Serialize(payload));
            return Ok(new { ok = true });
        }

        [HttpPost("cancelOrder")]
        public IActionResult CancelOrderEvent([FromBody] FoodOrderResponse body)
        {
            var payload = JsonSerializer.Serialize(new
            {
                source = "Getir",
                kind = "cancel",
                code = body.confirmationId,
                total = body.totalPrice,
                at = DateTime.UtcNow
            }, JsonOpts);

            _orderStream.Publish(payload);
            return Ok(new { ok = true });
        }

        // =====================================================================
        // ========================  FOOD ORDERS (GETIR)  =======================
        // =====================================================================

        [HttpPost("food-orders/active")]
        public async Task<List<FoodOrderResponse>> GetActiveOrders()
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.PostAsync($"{BaseUrl}/food-orders/active",
                new StringContent("", Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new Exception(body);

            return JsonSerializer.Deserialize<List<FoodOrderResponse>>(body, JsonOpts) ?? new();
        }

        [HttpPost("food-orders/periodic/unapproved")]
        public async Task<List<FoodOrderResponse>> GetUnapprovedOrders()
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.PostAsync($"{BaseUrl}/food-orders/periodic/unapproved",
                new StringContent("", Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new Exception(body);

            return JsonSerializer.Deserialize<List<FoodOrderResponse>>(body, JsonOpts) ?? new();
        }

        [HttpPost("food-orders/periodic/cancelled")]
        public async Task<List<FoodOrderResponse>> GetCancelledOrders()
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.PostAsync($"{BaseUrl}/food-orders/periodic/cancelled",
                new StringContent("", Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) throw new Exception(body);

            return JsonSerializer.Deserialize<List<FoodOrderResponse>>(body, JsonOpts) ?? new();
        }

        [HttpGet("order-detail/{foodOrderId}")]
        public async Task<IActionResult> GetOrderById(string foodOrderId)
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.GetAsync($"{BaseUrl}/food-orders/{foodOrderId}");
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

            return Content(body, "application/json");
        }

        [HttpGet("food-orders/{foodOrderId}/cancel-options")]
        public async Task<IActionResult> GetCancelOptions([FromRoute] string foodOrderId)
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.GetAsync($"{BaseUrl}/food-orders/{foodOrderId}/cancel-options");
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);
            return Content(body, "application/json");
        }

        [HttpPost("cancel-order/{foodOrderId}/{cancelNote}/{cancelReasonId}")]
        public async Task<IActionResult> CancelOrder(
            [FromRoute] string foodOrderId,
            [FromRoute] string cancelNote,
            [FromRoute] string cancelReasonId)
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var cancelBody = new CancelOrderRequest
            {
                cancelNote = cancelNote,
                cancelReasonId = cancelReasonId,
                productId = foodOrderId // (Getir tarafında gövde şeması böyle)
            };

            var json = JsonSerializer.Serialize(cancelBody, JsonOpts);
            var resp = await http.PostAsync($"{BaseUrl}/food-orders/{foodOrderId}/cancel",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

            return Content(body, "application/json");
        }

        private async Task<IActionResult> UpdateOrderStatus(string foodOrderId, string actionEndpoint)
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var url = $"{BaseUrl}/food-orders/{foodOrderId}/{actionEndpoint}";
            var resp = await http.PostAsync(url, null);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);
            return Content(body, "application/json");
        }

        [HttpPost("food-orders/{foodOrderId}/verify/{status}")]
        public async Task<IActionResult> VerifyOrder([FromRoute] string foodOrderId, [FromRoute] int status)
        {
            if (status == 400) return await UpdateOrderStatus(foodOrderId, "verify");
            if (status == 325) return await UpdateOrderStatus(foodOrderId, "verify-scheduled");
            return Ok(new { ignored = true, foodOrderId, status });
        }

        [HttpPost("food-orders/{foodOrderId}/prepare")]
        public Task<IActionResult> PrepareOrder(string foodOrderId) =>
            UpdateOrderStatus(foodOrderId, "prepare");

        [HttpPost("food-orders/{foodOrderId}/deliver")]
        public Task<IActionResult> DeliverOrder(string foodOrderId) =>
            UpdateOrderStatus(foodOrderId, "deliver");

        [HttpPost("food-orders/{foodOrderId}/handover")]
        public Task<IActionResult> HandoverOrder(string foodOrderId) =>
            UpdateOrderStatus(foodOrderId, "handover");

        // =====================================================================
        // =========================  RESTAURANT INFO  ==========================
        // =====================================================================

        [HttpGet("restaurants")]
        public async Task<IActionResult> GetRestaurantInfo()
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.GetAsync($"{BaseUrl}/restaurants");
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);
            return Content(body, "application/json");
        }

        [HttpPost("restaurant")]
        public IActionResult StatusChange([FromBody] PosStatusChangeModel statusChange)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(statusChange));
            return Ok();
        }

        // =====================================================================
        // =========================  MENU / PRODUCTS  ==========================
        // =====================================================================

        [HttpGet("restaurants/menu")]
        public async Task<IActionResult> GetRestaurantMenu()
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.GetAsync($"{BaseUrl}/restaurants/menu");
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);
            return Content(body, "application/json");
        }

        [HttpGet("products/{productId}/status")]
        public async Task<IActionResult> GetProductStatus([FromRoute] string productId)
        {
            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var resp = await http.GetAsync($"{BaseUrl}/products/{productId}/status");
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);
            return Content(body, "application/json");
        }

        [HttpPut("products/{productId}/status")]
        public async Task<IActionResult> UpdateProductStatus([FromRoute] string productId, [FromBody] UpdateProductStatusRequest req)
        {
            if (req is null || (req.status != 100 && req.status != 200 && req.status != 400))
                return BadRequest("status must be 100|200|400.");

            var token = await GetTokenAsync();
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("token", token);

            var json = JsonSerializer.Serialize(req, JsonOpts);
            var resp = await http.PutAsync($"{BaseUrl}/products/{productId}/status",
                new StringContent(json, Encoding.UTF8, "application/json"));

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

            return string.IsNullOrWhiteSpace(body)
                ? Ok(new { id = productId, status = req.status })
                : Content(body, "application/json");
        }

        // =====================================================================
        // ==========================  HISTORY / REPORT  ========================
        // =====================================================================

        [HttpGet("food-orders/history-csv")]
        [Produces("text/csv")]
        public async Task<IActionResult> DownloadOrdersCsv(
            [FromQuery] string startDate,
            [FromQuery] string endDate,
            [FromQuery] string restaurantIds,
            [FromQuery] string? templateName)
        {
            var token = await GetTokenAsync();

            // query string build
            var qs = new List<string>
            {
                $"startDate={Uri.EscapeDataString(startDate)}",
                $"endDate={Uri.EscapeDataString(endDate)}",
                $"restaurantIds={Uri.EscapeDataString(restaurantIds)}"
            };
            if (!string.IsNullOrWhiteSpace(templateName))
                qs.Add($"templateName={Uri.EscapeDataString(templateName)}");

            var url = $"{BaseUrl}/food-orders/report/details?{string.Join("&", qs)}";

            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("token", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            var fileName = $"orders-{startDate}-{endDate}.csv";

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());

            var mediaType = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = await resp.Content.ReadAsStringAsync();
                var csv = JsonArrayToCsv(json);
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
                return File(bytes, "text/csv", fileName);
            }

            var raw = await resp.Content.ReadAsByteArrayAsync();
            return File(raw, string.IsNullOrWhiteSpace(mediaType) ? "text/csv" : mediaType, fileName);
        }

        [HttpGet("food-orders/history-rows")]
        [Produces("application/json")]
        public async Task<IActionResult> GetHistoryRows(
            [FromQuery] string startDate,
            [FromQuery] string endDate,
            [FromQuery] string restaurantIds,
            [FromQuery] string? templateName)
        {
            if (DateTime.TryParse(startDate, out var s) &&
                DateTime.TryParse(endDate, out var e) &&
                (e - s).TotalDays > 7)
            {
                return BadRequest(new { code = 13, error = "FinancialReportMaxDaysError", message = "maximum day limit is 7 days" });
            }

            var token = await GetTokenAsync();

            var qs = new List<string>
            {
                $"startDate={Uri.EscapeDataString(startDate)}",
                $"endDate={Uri.EscapeDataString(endDate)}",
                $"restaurantIds={Uri.EscapeDataString(restaurantIds)}"
            };
            templateName ??= "RestaurantFinancial";
            if (!string.IsNullOrWhiteSpace(templateName))
                qs.Add($"templateName={Uri.EscapeDataString(templateName)}");

            var url = $"{BaseUrl}/food-orders/report/details?{string.Join("&", qs)}";

            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("token", token);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            var body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return StatusCode((int)resp.StatusCode, body);

            return Content(body, "application/json; charset=utf-8");
        }

        private static string JsonArrayToCsv(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "";

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

            var extras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                foreach (var p in row.EnumerateObject())
                    if (!preferred.Contains(p.Name) && !extras.Contains(p.Name))
                        extras.Add(p.Name);
            }

            var headers = preferred.Concat(extras).ToList();
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var cells = new List<string>(headers.Count);
                foreach (var h in headers)
                {
                    string val = "";
                    if (row.ValueKind == JsonValueKind.Object &&
                        row.TryGetProperty(h, out var el))
                    {
                        val = el.ValueKind switch
                        {
                            JsonValueKind.String => el.GetString() ?? "",
                            JsonValueKind.Number => el.ToString(),
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            JsonValueKind.Null => "",
                            _ => el.ToString()
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
            if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
                return $"\"{s.Replace("\"", "\"\"")}\"";
            return s;
        }

        // =====================================================================
        // ============================  RECEIPT PNG  ===========================
        // =====================================================================

        [HttpGet("food-orders/{foodOrderId}/receipt.png")]
        public async Task<IActionResult> GetOrderReceiptPng(string foodOrderId)
        {
            var token = await GetTokenAsync();

            // sipariş
            FoodOrderResponse order;
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Add("token", token);
                var resp = await http.GetAsync($"{BaseUrl}/food-orders/{foodOrderId}");
                var body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, body);

                order = JsonSerializer.Deserialize<FoodOrderResponse>(body, JsonOpts)!;
            }

            // logo (opsiyonel)
            byte[]? logoBytes = null;
            try
            {
                var logoPath = Path.Combine(_env.WebRootPath, "uploads", "getir-logo.png");
                if (System.IO.File.Exists(logoPath))
                    logoBytes = await System.IO.File.ReadAllBytesAsync(logoPath);
            }
            catch { /* ignore */ }

            var png = ReceiptImageBuilder.BuildPng(order, logoBytes);
            return File(png, "image/png", $"siparis_fisi_{order.id}.png");
        }

        public static class ReceiptImageBuilder
        {
            public static byte[] BuildPng(FoodOrderResponse o, byte[]? logoBytes)
            {
                var tr = CultureInfo.GetCultureInfo("tr-TR");
                string Money(decimal v) => string.Format(tr, "{0:N2} ₺", v);
                static string TryDate(string? iso) =>
                    DateTime.TryParse(iso, out var dt) ? dt.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("tr-TR")) : "-";

                var phone = o?.client?.clientPhoneNumber ?? o?.client?.contactPhoneNumber ?? "-";
                var payment = o?.paymentMethodText?.tr ?? o?.paymentMethodText?.en
                              ?? (o?.paymentMethod > 0 ? $"Ödeme Yöntemi: {o.paymentMethod}" : "-");

                var deliveryType = o?.deliveryType == 2
                    ? "Restoran Kuryesi"
                    : $"Getir Kuryesi{(string.IsNullOrWhiteSpace(o?.courier?.name) ? "" : $": {o!.courier!.name}")}";

                var discountedTotal = (o?.totalDiscountedPrice ?? 0m) > 0 ? o!.totalDiscountedPrice : o!.totalPrice;
                var discount = Math.Max(0m, (o?.totalPrice ?? 0m) - (discountedTotal ?? 0m));
                var hasDiscount = discount > 0;
                var items = o?.products ?? new List<FoodProduct>();

                var doc = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Margin(30);
                        page.Size(PageSizes.A4);

                        // Header
                        page.Header().Row(row =>
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

                        // Content
                        page.Content().PaddingVertical(10).Column(col =>
                        {
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
                                if (!string.IsNullOrWhiteSpace(o?.clientNote)) R("Sipariş Notu", o!.clientNote!);
                                R("Müşteri", o?.client?.name ?? "-");
                                R("Telefon", phone);
                                if (!string.IsNullOrWhiteSpace(o?.clientNote)) R("Müşteri Notu", o!.clientNote!);
                            });

                            col.Item().PaddingVertical(8).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);

                            col.Item().Text("Ürünler").SemiBold().FontSize(12);
                            col.Item().Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(1);
                                    c.RelativeColumn(5);
                                    c.RelativeColumn(2);
                                    c.RelativeColumn(2);
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
                                    var lineTotal = (p?.totalPriceWithOption ?? 0m) > 0 ? p!.totalPriceWithOption!.Value : unitPrice * adet;

                                    t.Cell().Text(adet.ToString());
                                    t.Cell().Text(name);
                                    t.Cell().Text(Money(unitPrice));
                                    t.Cell().Text(Money(lineTotal));
                                }
                            });

                            col.Item().AlignRight().Column(rcol =>
                            {
                                rcol.Item().Text($"Ara Toplam: {Money((o!.totalPrice ?? 0m))}").FontSize(10);
                                if (hasDiscount)
                                    rcol.Item().Text($"İndirim: -{Money(discount)}").FontSize(10);
                                rcol.Item().Text($"İndirimli Toplam: {Money(discountedTotal ?? 0m)}").FontSize(12).SemiBold();
                            });

                            col.Item().PaddingTop(10).Text("Not: Bu fiş Getir test verilerinden üretilmiştir.").FontSize(9).Light();
                        });

                        // Footer
                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("© ").FontSize(9);
                            x.Span(DateTime.Now.ToString("yyyy"));
                        });
                    });
                });

                var images = doc.GenerateImages();
                return images.First();
            }
        }

        // =====================================================================
        // ========================  COURIER NOTIFICATION  ======================
        // =====================================================================

        [HttpGet("courier/latest/{orderId}")]
        public ActionResult<CourierNotificationDto> GetLatest(string orderId)
        {
            return _latest.TryGetValue(orderId, out var dto)
                ? dto
                : NotFound($"No notification found for order ID: {orderId}");
        }

        [HttpPost("courier")]
        public IActionResult CourierNotification([FromBody] CourierNotificationDto payload)
        {
            if (payload == null) return BadRequest("payload empty");

            Console.WriteLine("=== Courier Notification ===");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));

            if (!DateTimeOffset.TryParse(payload.CalculationDate, out _))
                Console.WriteLine("calculationDate is not a valid ISO-8601 datetime.");

            _latest[payload.OrderId] = payload;
            return Ok();
        }

        // =====================================================================
        // ================================  MAP  ===============================
        // =====================================================================

        [HttpPost("map/route-for-order")]
        public async Task<ActionResult<OrderRouteResponseDto>> RouteForOrder(
            [FromBody] OrderRouteRequestDto body,
            CancellationToken ct)
        {
            if (body?.Order == null) return BadRequest("order gerekli");
            var mode = (body.Mode ?? "r2c").Trim().ToLowerInvariant();

            // TO (Customer)
            (double lat, double lng)? to = GetCustomerLatLng(body.Order);
            string? toAddr = null;

            if (to == null)
            {
                toAddr = BuildCustomerAddressString(body.Order);
                var g = await GeocodeAsync(toAddr ?? string.Empty, ct);
                if (g == null) return BadRequest("Müşteri konumu/adresi çözülemedi.");
                to = g.Value;
            }

            // FROM
            (double lat, double lng)? from;
            string fromLabel;

            switch (mode)
            {
                case "r2c":
                    // Restoran konumu = KURYE konumu (senden gelen kural)
                    from = GetCourierLatLng(body.Order);
                    fromLabel = "Restaurant(=Courier)";
                    if (from == null) return BadRequest("Kurye konumu yok, r2c hesaplanamaz.");
                    break;

                case "c2c":
                    from = GetCourierLatLng(body.Order);
                    fromLabel = "Courier";
                    if (from == null) return BadRequest("Kurye konumu yok, c2c hesaplanamaz.");
                    break;

                default:
                    return BadRequest("mode geçersiz. 'r2c' veya 'c2c' kullanın.");
            }

            var osrm = await OsrmRouteAsync(from!.Value, to!.Value, ct);
            if (osrm == null) return StatusCode(502, "Rota hesaplanamadı.");

            var (dist, dur, coords) = osrm.Value;
            var resp = new OrderRouteResponseDto
            {
                From = new MapPointDto { Lat = from.Value.lat, Lng = from.Value.lng, Label = fromLabel },
                To = new MapPointDto { Lat = to.Value.lat, Lng = to.Value.lng, Label = "Customer", Address = toAddr },
                DistanceMeters = dist,
                DurationSeconds = dur,
                Coordinates = coords
            };
            return Ok(resp);
        }

        private static (double lat, double lng)? GetCustomerLatLng(FoodOrderResponse o)
        {
            var loc = o?.client?.location;
            return (loc?.lat != null && loc?.lon != null) ? (loc.lat.Value, loc.lon.Value) : null;
        }

        private static (double lat, double lng)? GetCourierLatLng(FoodOrderResponse o)
        {
            var loc = o?.courier?.location;
            return (loc?.lat != null && loc?.lon != null) ? (loc.lat.Value, loc.lon.Value) : null;
        }

        private static string? BuildCustomerAddressString(FoodOrderResponse o)
        {
            var a = o?.client?.deliveryAddress;
            if (a == null) return null;

            string Join(params string?[] parts) =>
                string.Join(", ", parts.Where(x => !string.IsNullOrWhiteSpace(x))!);

            return Join(
                a.address,
                string.IsNullOrWhiteSpace(a.doorNo) ? null : $"Kapı {a.doorNo}",
                string.IsNullOrWhiteSpace(a.aptNo) ? null : $"Daire {a.aptNo}",
                string.IsNullOrWhiteSpace(a.floor) ? null : $"Kat {a.floor}",
                a.district,
                a.city
            );
        }

        private async Task<(double lat, double lng)?> GeocodeAsync(string address, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;

            var url = $"https://nominatim.openstreetmap.org/search?format=json&limit=1&q={Uri.EscapeDataString(address)}";
            using var resp = await _httpMap.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;

            var first = doc.RootElement[0];
            var latStr = first.GetProperty("lat").GetString();
            var lonStr = first.GetProperty("lon").GetString();

            if (double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                return (lat, lon);

            return null;
        }

        private async Task<(double distance, double duration, List<double[]> coords)?> OsrmRouteAsync(
            (double lat, double lng) from, (double lat, double lng) to, CancellationToken ct)
        {
            string ToLonLat((double lat, double lng) p) =>
                $"{p.lng.ToString(CultureInfo.InvariantCulture)},{p.lat.ToString(CultureInfo.InvariantCulture)}";

            var url = $"https://router.project-osrm.org/route/v1/driving/{ToLonLat(from)};{ToLonLat(to)}?overview=full&geometries=geojson";
            using var resp = await _httpMap.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var routes = doc.RootElement.GetProperty("routes");
            if (routes.GetArrayLength() == 0) return null;

            var r0 = routes[0];
            var dist = r0.GetProperty("distance").GetDouble();   // meters
            var dur = r0.GetProperty("duration").GetDouble();   // seconds
            var geom = r0.GetProperty("geometry").GetProperty("coordinates"); // [ [lon,lat], ... ]

            var coords = new List<double[]>(geom.GetArrayLength());
            foreach (var pt in geom.EnumerateArray())
            {
                var lon = pt[0].GetDouble();
                var lat = pt[1].GetDouble();
                coords.Add(new[] { lat, lon }); // Leaflet: [lat, lng]
            }

            return (dist, dur, coords);
        }
    }
}



// === HARİTA / ROTA: Controller içi yardımcı tipler ===
public class MapPointDto
    {
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public string? Label { get; set; } // "Courier" | "Customer"
        public string? Address { get; set; } // geocode için
    }

    public class OrderRouteRequestDto
    {
        // mode: "r2c" (Restaurant(=Courier) -> Customer) | "c2c" (Courier -> Customer)
        public string? Mode { get; set; }
        public FoodOrderResponse? Order { get; set; }
    }

    public class OrderRouteResponseDto
    {
        public MapPointDto From { get; set; } = new();
        public MapPointDto To { get; set; } = new();
        public double DistanceMeters { get; set; }
        public double DurationSeconds { get; set; }
        public List<double[]> Coordinates { get; set; } = new(); // [lat,lng]
    }

    // Tek bir HttpClient yeterli
   
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

