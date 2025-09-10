namespace PluginTest.Options
{
    public sealed class YemeksepetiOptions
    {
        public string LoginUrl { get; set; } = default!;
        public string Username { get; set; } = default!;
        public string Password { get; set; } = default!;
       
    }
    public class YemeksepetiMapOptions
    {
        public double? RestaurantLat { get; set; } = 41.068001;
        public double? RestaurantLng { get; set; } = 29.041180;
        public string? RestaurantAddress { get; set; }  // İstersen fallback geocode için
    }
}
