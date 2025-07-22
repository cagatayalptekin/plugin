using Microsoft.AspNetCore.Mvc;

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
        [HttpPost("order")]
        public IActionResult ReceiveOrder([FromBody] YemeksepetiOrderModel order)
        {
            if (order == null)
                return BadRequest("Sipariş verisi boş.");

            // Burada loglama, veritabanına kaydetme vb. yapılabilir
            return Ok(new
            {
                status = "success",
                source = "yemeksepeti",
                received = order
            });
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
}

