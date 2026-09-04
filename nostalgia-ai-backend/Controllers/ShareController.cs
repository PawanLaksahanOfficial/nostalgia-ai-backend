using Application.DTOs;
using Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace nostalgia_ai_backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class ShareController : ControllerBase
    {
        private readonly IShareLinkService _shareLinkService;
        private readonly IFileStorage _fileStorage;

        public ShareController(IShareLinkService shareLinkService, IFileStorage fileStorage)
        {
            _shareLinkService = shareLinkService;
            _fileStorage = fileStorage;
        }

        [HttpGet("{token}")]
        [EnableRateLimiting("share-view")]
        public async Task<ActionResult<ApiResponse<PublicVideoDto>>> GetSharedVideo(string token)
        {
            var video = await _shareLinkService.ResolveAsync(token);
            if (video == null)
            {
                return NotFound(ApiResponse<PublicVideoDto>.NotFound(
                    "This link is no longer available."));
            }
            return Ok(ApiResponse<PublicVideoDto>.Ok(video));
        }

        [HttpGet("{token}/stream")]
        [DisableRateLimiting]
        public async Task<IActionResult> StreamSharedVideo(string token) =>
            await ServeAsync(token, ShareMediaKind.Video, asAttachment: false);

        [HttpGet("{token}/download")]
        [DisableRateLimiting]
        public async Task<IActionResult> DownloadSharedVideo(string token) =>
            await ServeAsync(token, ShareMediaKind.Video, asAttachment: true);

        [HttpGet("{token}/thumbnail")]
        [DisableRateLimiting]
        public async Task<IActionResult> GetSharedThumbnail(string token) =>
            await ServeAsync(token, ShareMediaKind.Thumbnail, asAttachment: false);

        private async Task<IActionResult> ServeAsync(string token, ShareMediaKind kind, bool asAttachment)
        {
            var target = await _shareLinkService.ResolveMediaAsync(token, kind);
            if (target == null)
            {
                return NotFound(ApiResponse<object>.NotFound("This link is no longer available."));
            }
            var stream = await _fileStorage.DownloadAsync(target.StorageKey);
            if (stream == null)
            {
                return NotFound(ApiResponse<object>.NotFound("This link is no longer available."));
            }
            return asAttachment
                ? File(stream, target.ContentType, target.FileName, enableRangeProcessing: true)
                : File(stream, target.ContentType, enableRangeProcessing: true);
        }
    }
}
