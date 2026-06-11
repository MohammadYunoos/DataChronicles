using DataChronicles.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace DataChronicles.Api.Controllers;

[ApiController]
[Route("api/chat")]
public class ChatController : ControllerBase
{
    private readonly ChatService _chat;

    public ChatController(ChatService chat) => _chat = chat;

    public record ChatRequest(string Question, string? BatchId);

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatRequest request)
    {
        var answer = await _chat.AnswerAsync(request.Question, request.BatchId);
        return Ok(new { answer });
    }
}
