using Microsoft.AspNetCore.Mvc;

namespace PluginTest.Controllers
{
    [ApiController]
    [Route("api/getir")]
    public class GetirController : ControllerBase
    {
        private const string ApiKey = "X7kL93-fgh8W-Zmq0P-Ak2N9";

        private bool IsAuthorized()
        {
            return Request.Headers.TryGetValue("x-api-key", out var apiKey) && apiKey == ApiKey;
        }

        // 1. Yeni Sipariş Webhook (birden fazla sipariş olabilir)
        [HttpPost("newOrder")]
        public IActionResult NewOrder([FromBody] List<GetirOrderModel> orders)
        {
            if (!IsAuthorized()) return Unauthorized();

            // Siparişleri işle
            return Ok(new { status = "orders received", count = orders.Count });
        }

        // 2. Sipariş İptali Webhook
        [HttpPost("cancelOrder")]
        public IActionResult CancelOrder([FromBody] CancelRequest cancel)
        {
            if (!IsAuthorized()) return Unauthorized();

            return Ok(new { status = "cancel received", cancel.OrderId });
        }

        // 3. Kurye Restorana Ulaştı Webhook
        [HttpPost("courierArrived")]
        public IActionResult CourierArrived([FromBody] CourierArrivalModel courier)
        {
            if (!IsAuthorized()) return Unauthorized();

            return Ok(new { status = "courier arrived", courier.OrderId, courier.ArrivalTime });
        }

        // 4. Restoran Statü Değişikliği Bildirimi Webhook
        [HttpPost("statusChange")]
        public IActionResult StatusChange([FromBody] PosStatusChangeModel statusChange)
        {
            if (!IsAuthorized()) return Unauthorized();

            return Ok(new { status = "restaurant status received", restaurant = statusChange.RestaurantSecretKey });
        }
    }

    public class GetirOrderModel
    {
        public string? Id { get; set; }
        public int Status { get; set; }
        public bool IsScheduled { get; set; }
        public string? ConfirmationId { get; set; }
        public ClientInfo? Client { get; set; }
        public CourierInfo? Courier { get; set; }
        public List<OrderProduct>? Products { get; set; }
        public string? ClientNote { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TotalDiscountedPrice { get; set; }
        public string? CheckoutDate { get; set; }
        public string? ScheduledDate { get; set; }
        public string? VerifyDate { get; set; }
        public string? ScheduleVerifiedDate { get; set; }
        public string? PrepareDate { get; set; }
        public string? HandoverDate { get; set; }
        public string? ReachDate { get; set; }
        public string? DeliverDate { get; set; }
        public int DeliveryType { get; set; }
        public bool IsEcoFriendly { get; set; }
        public bool DoNotKnock { get; set; }
        public Restaurant? Restaurant { get; set; }
        public int PaymentMethod { get; set; }
        public object? PaymentMethodText { get; set; }
        public string? CancelNote { get; set; }
        public CancelReason? CancelReason { get; set; }
        public bool RestaurantPanelOperation { get; set; }
        public Brand? Brand { get; set; }
        public bool IsQueued { get; set; }
        public int CalculatedCourierToRestaurantETA { get; set; }
    }

    public class ClientInfo
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public Location? Location { get; set; }
        public string? ClientPhoneNumber { get; set; }
        public string? ClientUnmaskedPhoneNumber { get; set; }
        public string? ContactPhoneNumber { get; set; }
        public DeliveryAddress? DeliveryAddress { get; set; }
    }

    public class CourierInfo
    {
        public string? Id { get; set; }
        public int Status { get; set; }
        public string? Name { get; set; }
        public Location? Location { get; set; }
    }

    public class Location
    {
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public class DeliveryAddress
    {
        public string? Id { get; set; }
        public string? Address { get; set; }
        public string? AptNo { get; set; }
        public string? Floor { get; set; }
        public string? DoorNo { get; set; }
        public string? City { get; set; }
        public string? District { get; set; }
        public string? Description { get; set; }
    }

    public class OrderProduct
    {
        public string? Id { get; set; }
        public string? ImageURL { get; set; }
        public string? WideImageURL { get; set; }
        public int Count { get; set; }
        public string? Product { get; set; }
        public string? ChainProduct { get; set; }
        public LocalizedText? Name { get; set; }
        public decimal Price { get; set; }
        public decimal OptionPrice { get; set; }
        public decimal PriceWithOption { get; set; }
        public decimal TotalPrice { get; set; }
        public decimal TotalOptionPrice { get; set; }
        public decimal TotalPriceWithOption { get; set; }
        public List<object>? OptionCategories { get; set; }
        public DisplayInfo? DisplayInfo { get; set; }
        public string? Note { get; set; }
    }

    public class LocalizedText
    {
        public string? Tr { get; set; }
        public string? En { get; set; }
    }

    public class DisplayInfo
    {
        public LocalizedText? Title { get; set; }
        public Dictionary<string, List<string>>? Options { get; set; }
    }

    public class CancelReason
    {
        public string? Id { get; set; }
        public LocalizedText? Messages { get; set; }
    }

    public class Restaurant
    {
        public string? Id { get; set; }
    }

    public class Brand
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    public class CancelRequest
    {
        public string? CancelNote { get; set; }
        public string? CancelReasonId { get; set; }
        public string? ProductId { get; set; }

        public string? OrderId => ProductId;
    }

    public class CourierArrivalModel
    {
        public string? OrderId { get; set; }
        public DateTime ArrivalTime { get; set; }
    }

    public class PosStatusChangeModel
    {
        public string? AppSecretKey { get; set; }
        public string? RestaurantSecretKey { get; set; }
    }


}
