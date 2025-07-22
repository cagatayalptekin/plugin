using Microsoft.AspNetCore.Mvc;

namespace PluginTest.Controllers
{
    [ApiController]
    [Route("api/getir")]
    public class GetirController : ControllerBase
    {
        private const string ApiKey = "X7kL93-fgh8W-Zmq0P-Ak2N9"; // Burayı gerçek API anahtarınla değiştir

        // Yeni Sipariş
        [HttpPost("newOrder")]
        public IActionResult NewOrder([FromBody] GetirOrderModel order)
        {
            if (!IsAuthorized()) return Unauthorized();

            // Burada sipariş işleme kodun olabilir
            return Ok(new { status = "order received", order });
        }

        // Sipariş İptali
        [HttpPost("cancelOrder")]
        public IActionResult CancelOrder([FromBody] CancelModel cancel)
        {
            if (!IsAuthorized()) return Unauthorized();

            return Ok(new { status = "order canceled", orderId = cancel.OrderId });
        }

        // Kurye Varış Bildirimi
        [HttpPost("courierArrived")]
        public IActionResult CourierArrived([FromBody] CourierArrivalModel arrival)
        {
            if (!IsAuthorized()) return Unauthorized();

            return Ok(new
            {
                status = "courier arrived",
                orderId = arrival.OrderId,
                time = arrival.ArrivalTime
            });
        }

        // Restoran Statü Değişikliği
        [HttpPost("statusChange")]
        public IActionResult StatusChange([FromBody] StatusChangeModel status)
        {
            if (!IsAuthorized()) return Unauthorized();

            return Ok(new
            {
                status = "status updated",
                restaurantId = status.RestaurantId,
                newStatus = status.NewStatus
            });
        }

        // x-api-key kontrolü
        private bool IsAuthorized()
        {
            return Request.Headers.TryGetValue("x-api-key", out var apiKey) && apiKey == ApiKey;
        }
    }

    // MODELLER

    public class GetirOrderModel
    {
        public string OrderId { get; set; }
        public string CustomerName { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
        public List<string> Items { get; set; }
        public string Note { get; set; }
    }

    public class CancelModel
    {
        public string OrderId { get; set; }
    }

    public class CourierArrivalModel
    {
        public string OrderId { get; set; }
        public DateTime ArrivalTime { get; set; }
    }

    public class StatusChangeModel
    {
        public string RestaurantId { get; set; }
        public string NewStatus { get; set; } // Örn: open, closed, busy
    }
}
