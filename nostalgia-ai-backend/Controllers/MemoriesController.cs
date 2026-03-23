using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace nostalgia_ai_backend.Controllers
{

    [ApiController]
    [Route("api/[controller]")]
    public class MemoriesController : ControllerBase
    {
        private readonly IAIService _aIService;

        public MemoriesController(IAIService aIService) 
        {
            _aIService = aIService;
        }

        [HttpPost("generate")]
        public async Task<ActionResult<string>> GenerateNostalgicFeeling([FromBody] string userPrompt)
        {
            try
            {
                var result = await _aIService.GenerateNostalgicTextAsync(userPrompt);
                return Ok(result);
            }
            catch (Exception ex) 
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
