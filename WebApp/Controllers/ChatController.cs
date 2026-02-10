using Microsoft.AspNetCore.Mvc;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Controllers;

public class ChatController : Controller
{
    private readonly IAgentService _agentService;
    private readonly ILogger<ChatController> _logger;
    private readonly IWebHostEnvironment _environment;

    public ChatController(IAgentService agentService, ILogger<ChatController> logger, IWebHostEnvironment environment)
    {
        _agentService = agentService;
        _logger = logger;
        _environment = environment;
    }

    public async Task<IActionResult> Index()
    {
        // Chat is now the default Home/Index page.
        return RedirectToAction(actionName: "Index", controllerName: "Home");
    }

    [HttpPost]
    [Route("api/upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded");
        }

        // Only allow image uploads for now.
        if (string.IsNullOrWhiteSpace(file.ContentType) || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only image uploads are supported.");
        }

        // 5MB limit per file.
        if (file.Length > 5 * 1024 * 1024)
        {
            return BadRequest("File too large. Max 5MB.");
        }

        // Create uploads directory if it doesn't exist
        var uploadsPath = Path.Combine(_environment.WebRootPath, "uploads");
        Directory.CreateDirectory(uploadsPath);

        // Generate unique filename
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var filePath = Path.Combine(uploadsPath, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new FileAttachment
        {
            FileName = file.FileName,
            ContentType = file.ContentType,
            Size = file.Length,
            StoragePath = $"/uploads/{fileName}"
        };

        _logger.LogInformation("File uploaded: {FileName} ({Size} bytes)", file.FileName, file.Length);

        return Ok(attachment);
    }

    [HttpGet]
    [Route("api/agents")]
    public async Task<IActionResult> GetAgents()
    {
        var agents = await _agentService.GetAvailableAgentsAsync();
        return Ok(agents);
    }
}
