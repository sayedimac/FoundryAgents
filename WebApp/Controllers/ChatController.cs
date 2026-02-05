using Microsoft.AspNetCore.Mvc;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Controllers;

public class ChatController : Controller
{
    private readonly IAgentService _agentService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IAgentService agentService, ILogger<ChatController> logger)
    {
        _agentService = agentService;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var agents = await _agentService.GetAvailableAgentsAsync();
        ViewBag.Agents = agents;
        return View();
    }

    [HttpPost]
    [Route("api/upload")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file uploaded");
        }

        // Create uploads directory if it doesn't exist
        var uploadsPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
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
