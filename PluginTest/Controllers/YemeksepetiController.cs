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
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
[ApiController]
[Route("api/yemeksepeti")]
public class YemeksepetiController : Controller
{
    private readonly IConfiguration _config;

    public List<string> OrderIdentifiers;
    public static List<YemeksepetiOrderModel> orders = new();
    public string token { get; set; }

    private readonly OrderStream _orderStream;
    private readonly IHttpClientFactory _http;
    private readonly YemeksepetiMapOptions _opt;

    public YemeksepetiController(
        OrderStream orderStream,
        IOptions<YemeksepetiMapOptions> opt,
        IHttpClientFactory http, IConfiguration config)
    {
        _orderStream = orderStream;
        _http = http;
        _opt = opt.Value;
        _config = config;
    }
    [HttpPost("order/{remoteId}")]
    public IActionResult PostOrder([FromRoute] string remoteId,YemeksepetiOrderModel body)
    {
     

        var payload = JsonSerializer.Serialize(new
        {
            source = "Yemeksepeti",
            kind = "new",
            code = body.code,
            body.price,
            at = DateTime.UtcNow,
            order=body
        });

        _orderStream.Publish(payload);
        orders.Add(body);
        try
        {
            SaveOrderToDbAsync(body);
        }
        catch (Exception ex)
        {
            // Loglayabilirsin
            Console.WriteLine("YS Order DB insert error: " + ex.Message);
        }
        return Ok(new { ok = true });
    }
    [HttpGet("orders/history")]
    public async Task<IActionResult> GetOrderHistory([FromQuery] string startDate, [FromQuery] string endDate,
    [FromQuery] string? restaurantId, [FromQuery] string? code)
    {
        if (!DateTime.TryParse(startDate, out var start)) return BadRequest("startDate hatalı");
        if (!DateTime.TryParse(endDate, out var end)) return BadRequest("endDate hatalı");

        var cs = _config.GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();

        var sql = @"
SELECT RawJson
FROM [ADABALIK].[dbo].[YEMEKSEPETI_ORDERS]
WHERE CreatedAtUtc >= @start AND CreatedAtUtc <= @end
";
        if (!string.IsNullOrWhiteSpace(restaurantId)) sql += " AND RestaurantId = @restaurantId";
        if (!string.IsNullOrWhiteSpace(code)) sql += " AND Code = @code";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@start", start);
        cmd.Parameters.AddWithValue("@end", end);
        if (!string.IsNullOrWhiteSpace(restaurantId)) cmd.Parameters.AddWithValue("@restaurantId", restaurantId);
        if (!string.IsNullOrWhiteSpace(code)) cmd.Parameters.AddWithValue("@code", code);

        var list = new List<YemeksepetiOrderModel>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var raw = r.GetString(0);
            try
            {
                var o = JsonSerializer.Deserialize<YemeksepetiOrderModel>(raw);
                if (o != null) list.Add(o);
            }
            catch { /* yut */ }
        }

        return Ok(list);
    }
    private async Task SaveOrderToDbAsync(YemeksepetiOrderModel model)
    {
        // ADABALIK > dbo.YEMEKSEPETI_ORDERS (alt SQL scriptte var)
        var cs = _config.GetConnectionString("DefaultConnection");
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();

        var rawJson = JsonSerializer.Serialize(model);

        var cmd = new SqlCommand(@"
INSERT INTO [ADABALIK].[dbo].[YEMEKSEPETI_ORDERS]
(
    Code,
    CreatedAtUtc,
    ExpectedDeliveryTimeUtc,
    RiderPickupTimeUtc,
    PickupTimeUtc,
    ExpeditionType,
    Platform,
    PlatformKey,
    CountryCode,
    CurrencySymbol,
    PaymentType,
    PaymentStatus,
    CustomerFirstName,
    CustomerLastName,
    CustomerEmail,
    CustomerMobile,
    AddressCity,
    AddressStreet,
    AddressNumber,
    AddressPostcode,
    GrandTotal,
    TotalNet,
    VatTotal,
    RiderTip,
    CollectFromCustomer,
    PayRestaurant,
    CorporateTaxId,
    RestaurantId,
    CustomerComment,
    RawJson
)
VALUES
(
    @Code,
    @CreatedAtUtc,
    @ExpectedDeliveryTimeUtc,
    @RiderPickupTimeUtc,
    @PickupTimeUtc,
    @ExpeditionType,
    @Platform,
    @PlatformKey,
    @CountryCode,
    @CurrencySymbol,
    @PaymentType,
    @PaymentStatus,
    @CustomerFirstName,
    @CustomerLastName,
    @CustomerEmail,
    @CustomerMobile,
    @AddressCity,
    @AddressStreet,
    @AddressNumber,
    @AddressPostcode,
    @GrandTotal,
    @TotalNet,
    @VatTotal,
    @RiderTip,
    @CollectFromCustomer,
    @PayRestaurant,
    @CorporateTaxId,
    @RestaurantId,
    @CustomerComment,
    @RawJson
);", conn);

        // paramlar
        cmd.Parameters.AddWithValue("@Code", (object?)model.code ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedAtUtc", (object?)model.createdAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExpectedDeliveryTimeUtc", (object?)model.delivery?.expectedDeliveryTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RiderPickupTimeUtc", (object?)model.delivery?.riderPickupTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PickupTimeUtc", (object?)model.pickup?.pickupTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ExpeditionType", (object?)model.expeditionType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Platform", (object?)model.localInfo?.platform ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PlatformKey", (object?)model.localInfo?.platformKey ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CountryCode", (object?)model.localInfo?.countryCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CurrencySymbol", (object?)model.localInfo?.currencySymbol ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PaymentType", (object?)model.payment?.type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PaymentStatus", (object?)model.payment?.status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CustomerFirstName", (object?)model.customer?.firstName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CustomerLastName", (object?)model.customer?.lastName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CustomerEmail", (object?)model.customer?.email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CustomerMobile", (object?)model.customer?.mobilePhone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AddressCity", (object?)model.delivery?.address?.city ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AddressStreet", (object?)model.delivery?.address?.street ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AddressNumber", (object?)model.delivery?.address?.number ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AddressPostcode", (object?)model.delivery?.address?.postcode ?? DBNull.Value);
        // Sayısal alanlar double/string gelebilir; parse etmeye çalış:
        cmd.Parameters.AddWithValue("@GrandTotal", TryDec(model.price?.grandTotal));
        cmd.Parameters.AddWithValue("@TotalNet", TryDec(model.price?.totalNet));
        cmd.Parameters.AddWithValue("@VatTotal", TryDec(model.price?.vatTotal));
        cmd.Parameters.AddWithValue("@RiderTip", TryDec(model.price?.riderTip));
        cmd.Parameters.AddWithValue("@CollectFromCustomer", TryDec(model.price?.collectFromCustomer));
        cmd.Parameters.AddWithValue("@PayRestaurant", TryDec(model.price?.payRestaurant));
        cmd.Parameters.AddWithValue("@CorporateTaxId", (object?)model.corporateTaxId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@RestaurantId", (object?)model.platformRestaurant?.id ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CustomerComment", (object?)model.comments?.customerComment ?? DBNull.Value);
        cmd.Parameters.Add("@RawJson", SqlDbType.NVarChar, -1).Value = (object)rawJson ?? DBNull.Value;

        await cmd.ExecuteNonQueryAsync();

        static object TryDec(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DBNull.Value;
            if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
            if (decimal.TryParse(s, out d)) return d;
            return DBNull.Value;
        }
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








    // ===========================
    // === MAP / ROUTE SECTION ===
    // ===========================

    public class MapPointDto
    {
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public string? Label { get; set; }   // "Restaurant" | "Customer"
        public string? Address { get; set; } // geocode te fallback için
    }

    public class YsOrderRouteRequestDto
    {
        // mode: "r2c" (Restaurant -> Customer). İlerde "c2c" eklenebilir.
        public string? Mode { get; set; }
        public YemeksepetiOrderModel? Order { get; set; }
    }

    public class YsOrderRouteResponseDto
    {
        public MapPointDto From { get; set; } = new();
        public MapPointDto To { get; set; } = new();
        public double DistanceMeters { get; set; }
        public double DurationSeconds { get; set; }
        public List<double[]> Coordinates { get; set; } = new(); // [lat,lng]
    }

    private static readonly HttpClient _httpMap = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
    });

    static YemeksepetiController()
    {
        _httpMap.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("pos-web-ui", "1.0"));
    }

    [HttpPost("map/route-for-order")]
    public async Task<ActionResult<YsOrderRouteResponseDto>> RouteForOrder([FromBody] YsOrderRouteRequestDto body, CancellationToken ct)
    {
        LOG("RouteForOrder() içindeyim.");
        LOG($"Body Raw JSON: {Trunc(JsonSerializer.Serialize(body))}");

        if (body?.Order == null)
        {
            LOG("Body veya Order null.");
            return BadRequest("Body ve order gerekli.");
        }

        var mode = (body.Mode ?? "r2c").Trim().ToLowerInvariant();
        LOG($"Mode => '{mode}'");
        if (mode != "r2c")
        {
            LOG("Yalnızca r2c destekleniyor.");
            return BadRequest("Yalnızca 'r2c' (Restoran→Müşteri) destekleniyor.");
        }

        // TO (Müşteri): adres stringini kur → geocode
        var toAddr = BuildCustomerAddressString(body.Order);
        LOG($"toAddr => '{toAddr}'");
        if (string.IsNullOrWhiteSpace(toAddr))
        {
            LOG("Müşteri adresi eksik.");
            return BadRequest("Müşteri adresi eksik.");
        }

        var to = await GeocodeAsync(toAddr!, ct);
        if (to == null)
        {
            LOG("Müşteri adresi çözülemedi.");
            return BadRequest("Müşteri adresi çözülemedi.");
        }
        LOG($"TO => lat={to.Value.lat}, lng={to.Value.lng}");

        // FROM (Restoran)
        (double lat, double lng)? from = null;
        LOG($"Options.RestaurantLat={_opt.RestaurantLat}, RestaurantLng={_opt.RestaurantLng}, RestaurantAddress='{_opt.RestaurantAddress}'");

        if (_opt.RestaurantLat.HasValue && _opt.RestaurantLng.HasValue)
        {
            from = (_opt.RestaurantLat.Value, _opt.RestaurantLng.Value);
            LOG($"FROM (opt lat/lng) => lat={from.Value.lat}, lng={from.Value.lng}");
        }
        else if (!string.IsNullOrWhiteSpace(_opt.RestaurantAddress))
        {
            var g = await GeocodeAsync(_opt.RestaurantAddress!, ct);
            if (g != null)
            {
                from = g.Value;
                LOG($"FROM (geocoded address) => lat={from.Value.lat}, lng={from.Value.lng}");
            }
        }

        if (from == null)
        {
            var msg = "Restoran konumu yapılandırılmadı. YemeksepetiOptions.RestaurantLat/Lng veya RestaurantAddress";
            LOG(msg);
            return BadRequest(msg);
        }

        // OSRM rota
        var route = await OsrmRouteAsync(from.Value, to.Value, ct);
        if (route == null)
        {
            LOG("Rota hesaplanamadı (OSRM).");
            return StatusCode(502, "Rota hesaplanamadı.");
        }

        var (dist, dur, coords) = route.Value;
        var resp = new YsOrderRouteResponseDto
        {
            From = new MapPointDto { Lat = from.Value.lat, Lng = from.Value.lng, Label = "Restaurant" },
            To = new MapPointDto { Lat = to.Value.lat, Lng = to.Value.lng, Label = "Customer", Address = toAddr },
            DistanceMeters = dist,
            DurationSeconds = dur,
            Coordinates = coords
        };

        LOG($"Response JSON: {Trunc(JsonSerializer.Serialize(resp))}");
        return Ok(resp);
    }


    private static string? BuildCustomerAddressString(YemeksepetiOrderModel o)
    {
        LOG("BuildCustomerAddressString() içindeyim.");
        var a = o?.delivery?.address;
        if (a == null) { LOG("delivery.address = NULL"); return null; }

        // Adres objesini bütünüyle logla:
        LOG("delivery.address JSON: " + Trunc(JsonSerializer.Serialize(a)));

        var street = a.street?.Trim();
        var numberRaw = a.number?.Trim();
        var city = a.city?.Trim();           // genelde "İstanbul"
        var postcode = a.postcode?.Trim();   // ör: 34394
                                             // İlçe/mahalle gibi ekstra alanlar varsa yakala:
        string? district = TryGetProp(a, "district") ?? TryGetProp(a, "ilce") ?? TryGetProp(a, "town");
        string? neighborhood = TryGetProp(a, "neighborhood") ?? TryGetProp(a, "mahalle");

        // number sadece rakamsa house number olarak kullan:
        string? houseNumber = (!string.IsNullOrWhiteSpace(numberRaw) && numberRaw.All(char.IsDigit)) ? numberRaw : null;
        if (houseNumber == null && !string.IsNullOrWhiteSpace(numberRaw))
            LOG($"number alanı sayısal değil ('{numberRaw}'), house number olarak kullanılmayacak.");

        // İlçe yoksa, bazı ZIP’ler için kestirim (ör: 34394 → Şişli)
        if (string.IsNullOrWhiteSpace(district) && postcode == "34394") district = "Şişli";

        // Kompozisyonu ILÇE’Yİ DAHİL EDEREK yap:
        string Join(params string?[] items) => string.Join(", ", items.Where(s => !string.IsNullOrWhiteSpace(s))!);

        var composed = Join(
            // Sokak + kapı no (varsa)
            string.IsNullOrWhiteSpace(houseNumber) ? street : $"{street} {houseNumber}",
            // Mahalleyi sokaktan sonra dene
            neighborhood,
            // İlçe → Şişli
            district,
            // Şehir → İstanbul
            city,
            postcode,
            "Türkiye"
        );

        LOG($"raw address fields => street='{street}', number='{numberRaw}', city='{city}', postcode='{postcode}', district='{district}', neighborhood='{neighborhood}'");
        LOG($"composed address: {composed}");
        return composed;
    }

    // Küçük yardımcı: a nesnesinde isme göre property çek
    private static string? TryGetProp(object obj, string name)
    {
        var p = obj.GetType().GetProperty(name, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
        var v = p?.GetValue(obj);
        return v?.ToString();
    }
    private YemeksepetiOrderModel _lastOrderForRoute; // RouteForOrder içinde set et

    private (double lat, double lng)? TryGetOrderCoordinates(YemeksepetiOrderModel o)
    {
        try
        {
            var a = o?.delivery?.address;
            if (a == null) { LOG("TryGetOrderCoordinates: address null"); return null; }

            double? lat = null, lng = null;

            foreach (var p in a.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var name = p.Name.ToLowerInvariant();
                var val = p.GetValue(a);
                if (val == null) continue;

                if (name.Contains("lat"))
                {
                    if (TryToDouble(val, out var d)) { lat = d; LOG($"TryGetOrderCoordinates: {p.Name}={d}"); }
                }
                if (name.Contains("lon") || name.Contains("lng"))
                {
                    if (TryToDouble(val, out var d)) { lng = d; LOG($"TryGetOrderCoordinates: {p.Name}={d}"); }
                }
            }

            if (lat.HasValue && lng.HasValue)
                return (lat.Value, lng.Value);

            LOG("TryGetOrderCoordinates: lat/lng bulunamadı.");
            return null;
        }
        catch (Exception ex)
        {
            LOG("TryGetOrderCoordinates hata: " + ex.Message);
            return null;
        }
    }

    private bool TryToDouble(object v, out double d)
    {
        switch (v)
        {
            case double dd: d = dd; return true;
            case float ff: d = ff; return true;
            case decimal mm: d = (double)mm; return true;
            case string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var x): d = x; return true;
            default: d = 0; return false;
        }
    }
    private async Task<(double lat, double lng)?> GeocodeAsync(string address, CancellationToken ct)
    {
        LOG("GeocodeAsync() içindeyim.");

        // 0) Order içinde koordinat varsa doğrudan kullan (çok kritik)
        var orderCoords = TryGetOrderCoordinates(_lastOrderForRoute); // _lastOrderForRoute: RouteForOrder içinde body.Order'ı set et.
        if (orderCoords != null)
        {
            LOG($"Order içinde koordinat bulundu => lat={orderCoords.Value.lat}, lng={orderCoords.Value.lng}");
            return orderCoords;
        }
        LOG("Order içinde koordinat bulunamadı; Nominatim'e geçiliyor.");

        if (string.IsNullOrWhiteSpace(address)) { LOG("address boş"); return null; }

        // 1) STRUCTURED SEARCH (sokak/ilçe/şehir/posta kodu ayrı)
        var parts = ParseAddress(address); // aşağıda
        var st1 = await GeocodeStructuredOnce(new()
        {
            ["street"] = BuildStreet(parts.Street, parts.HouseNo), // "Dede Korkut Sokak 12" ya da sadece sokak
            ["city"] = parts.District ?? parts.City,                // Şişli (varsa), yoksa İstanbul
            ["county"] = parts.City,                                // İstanbul
            ["postalcode"] = parts.Postcode,
            ["country"] = "Türkiye"
        }, "STRUCT#1", ct);
        if (st1 != null) return st1;

        // 2) STRUCTURED - alternatif kombinasyon
        var st2 = await GeocodeStructuredOnce(new()
        {
            ["street"] = parts.Street,
            ["city"] = parts.City,             // İstanbul
            ["state"] = parts.City,            // İstanbul'u state olarak da deneriz
            ["postalcode"] = parts.Postcode,
            ["country"] = "Türkiye"
        }, "STRUCT#2", ct);
        if (st2 != null) return st2;

        // 3) FREE-FORM (senin daha önceki denemelerine benzer ama "Sok./Sk." varyantlarını da dener)
        var ff1 = await GeocodeOnce(BuildFreeForm(parts, variant: 0), "TRY#FF1", ct);
        if (ff1 != null) return ff1;

        var ff2 = await GeocodeOnce(BuildFreeForm(parts, variant: 1), "TRY#FF2", ct); // Sok. / Sk. varyantı
        if (ff2 != null) return ff2;

        var ff3 = await GeocodeOnce(BuildFreeForm(parts, variant: 2), "TRY#FF3", ct); // postakodsuz
        return ff3;
    }

    private record AddrParts(string Street, string? HouseNo, string? District, string City, string? Postcode);
    private AddrParts ParseAddress(string composed)
    {
        // LOG’larda zaten tek tek alanları bastığımız için burada basitleştirilmiş parser yeterli
        // Ama yine de Sokak + no’yu ayırmayı deniyoruz (sokakta rakam varsa house no say)
        var m = Regex.Match(composed, @"^(?<street>.+?)\s+(?<no>\d+)\b", RegexOptions.IgnoreCase);
        string? house = m.Success ? m.Groups["no"].Value : null;

        // İlçe tahmini: "Şişli" geçiyorsa al, yoksa boş
        string? district = Regex.IsMatch(composed, @"\bŞişli\b", RegexOptions.IgnoreCase) ? "Şişli" : null;

        // Şehir: İstanbul varsayıyoruz
        const string city = "İstanbul";

        // Posta kodu: 5 hane
        var pm = Regex.Match(composed, @"\b(?<pc>\d{5})\b");
        string? pc = pm.Success ? pm.Groups["pc"].Value : null;

        // Sokak adı: “Sokak/Sok./Sk.” gibi varyantları koru
        var street = m.Success ? m.Groups["street"].Value : composed.Split(',')[0];

        return new AddrParts(street.Trim(), house, district, city, pc);
    }

    private string BuildStreet(string street, string? no)
        => string.IsNullOrWhiteSpace(no) ? street : $"{street} {no}";

    // Free-form varyantları üret
    private string BuildFreeForm(AddrParts p, int variant)
    {
        string sokak = p.Street;
        if (variant == 1) // “Sokak” → “Sok.”/“Sk.”
        {
            sokak = sokak.Replace("Sokak", "Sok.", StringComparison.OrdinalIgnoreCase)
                         .Replace("Sok.", "Sk.", StringComparison.OrdinalIgnoreCase);
        }
        if (variant == 2) // postakodsuz dene
            return $"{sokak}, {p.District ?? ""}, {p.City}, Türkiye";

        return $"{sokak}, {p.District ?? ""}, {p.City}, {p.Postcode}, Türkiye";
    }
    private async Task<(double lat, double lng)?> GeocodeStructuredOnce(
    Dictionary<string, string?> kv, string tag, CancellationToken ct)
    {
        var qs = string.Join("&",
            kv.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
              .Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value!)}"));

        var url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&addressdetails=1&accept-language=tr&countrycodes=tr&{qs}";
        LOG($"Geocode {tag} URL(structured) => {url}");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Yemeksepeti-Route/1.0 (+contact: your-email@example.com)");
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.AcceptLanguage.ParseAdd("tr");

        using var resp = await _httpMap.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        LOG($"Geocode {tag} HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        var body = await resp.Content.ReadAsStringAsync(ct);
        LOG($"Geocode {tag} response (trunc): {Trunc(body)}");

        if (!resp.IsSuccessStatusCode) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                var latStr = first.GetProperty("lat").GetString();
                var lonStr = first.GetProperty("lon").GetString();
                var display = first.TryGetProperty("display_name", out var dn) ? dn.GetString() : "";

                LOG($"Geocode {tag} candidate => display_name='{display}', lat='{latStr}', lon='{lonStr}'");

                if (double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                {
                    LOG($"Geocode {tag} OK => lat={lat}, lng={lon}");
                    return (lat, lon);
                }
            }
        }
        catch (Exception ex)
        {
            LOG($"Geocode {tag} JSON parse hatası: {ex.Message}");
        }
        return null;
    }

    // Elindeki GeocodeOnce(q=...) fonksiyonun aynen kalabilir (daha önce vermiştim).


    private async Task<(double lat, double lng)?> GeocodeOnce(string url, string tag, CancellationToken ct)
    {
        LOG($"Geocode {tag} URL => {url}");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        // Nominatim şartı: geçerli bir UA kullanın (tercihen iletişim içersin)
        req.Headers.TryAddWithoutValidation("User-Agent", "Yemeksepeti-Route/1.0 (+contact: your-email@example.com)");
        req.Headers.Referrer = new Uri("https://example.com/ys-plugin"); // opsiyonel ama faydalı
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.AcceptLanguage.ParseAdd("tr");

        using var resp = await _httpMap.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        LOG($"Geocode {tag} HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

        var body = await resp.Content.ReadAsStringAsync(ct);
        LOG($"Geocode {tag} response (trunc): {Trunc(body)}");

        if (!resp.IsSuccessStatusCode)
        {
            LOG($"Geocode {tag} başarısız (status={(int)resp.StatusCode}).");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                var first = doc.RootElement[0];
                var latStr = first.GetProperty("lat").GetString();
                var lonStr = first.GetProperty("lon").GetString();
                var display = first.TryGetProperty("display_name", out var dn) ? dn.GetString() : "";

                LOG($"Geocode {tag} candidate => display_name='{display}', lat='{latStr}', lon='{lonStr}'");

                if (double.TryParse(latStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                    double.TryParse(lonStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
                {
                    LOG($"Geocode {tag} OK => lat={lat}, lng={lon}");
                    return (lat, lon);
                }
                LOG($"Geocode {tag} parse edilemedi (lat/lon).");
            }
            else
            {
                LOG($"Geocode {tag}: sonuç dizisi boş.");
            }
        }
        catch (Exception ex)
        {
            LOG($"Geocode {tag} JSON parse hatası: {ex.Message}");
        }
        return null;
    }


    private async Task<(double distance, double duration, List<double[]> coords)?> OsrmRouteAsync(
     (double lat, double lng) from, (double lat, double lng) to, CancellationToken ct)
    {
        string ToLonLat((double lat, double lng) p) =>
            $"{p.lng.ToString(CultureInfo.InvariantCulture)},{p.lat.ToString(CultureInfo.InvariantCulture)}";

        var url = $"https://router.project-osrm.org/route/v1/driving/{ToLonLat(from)};{ToLonLat(to)}?overview=full&geometries=geojson";
        LOG($"OSRM URL => {url}");

        using var resp = await _httpMap.GetAsync(url, ct);
        LOG($"OSRM HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        var body = await resp.Content.ReadAsStringAsync(ct);
        LOG($"OSRM response (trunc): {Trunc(body)}");

        if (!resp.IsSuccessStatusCode) return null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var routes = doc.RootElement.GetProperty("routes");
            if (routes.GetArrayLength() == 0)
            {
                LOG("OSRM: routes[] boş.");
                return null;
            }

            var r0 = routes[0];
            var dist = r0.GetProperty("distance").GetDouble();  // meters
            var dur = r0.GetProperty("duration").GetDouble();  // seconds

            var geom = r0.GetProperty("geometry").GetProperty("coordinates"); // [ [lon,lat], ... ]
            var coords = new List<double[]>(geom.GetArrayLength());
            foreach (var pt in geom.EnumerateArray())
            {
                var lon = pt[0].GetDouble();
                var lat = pt[1].GetDouble();
                coords.Add(new[] { lat, lon }); // Leaflet: [lat,lng]
            }

            LOG($"OSRM OK => distance={dist} m, duration={dur} s, points={coords.Count}");
            return (dist, dur, coords);
        }
        catch (Exception ex)
        {
            LOG($"OSRM JSON parse hatası: {ex.Message}");
            return null;
        }
    }









    private static void LOG(string msg)
    => Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");

    private static string Trunc(string? s, int max = 600)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max) + "...(truncated)";
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
        Console.WriteLine(JsonSerializer.Serialize(orders));
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
