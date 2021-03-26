using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SubmissionEvaluation.Server.Classes.JekyllHandling;
using SubmissionEvaluation.Server.Classes.Messages;
using SubmissionEvaluation.Shared.Classes;

namespace SubmissionEvaluation.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "IsChallengePlattformUser")]
    public class DownloadController : Controller
    {
        private readonly ILogger _logger;

        public DownloadController(ILogger<DownloadController> logger)
        {
            _logger = logger;
        }

        [HttpGet("Help")]
        public IActionResult Help(string id, string path)
        {
            try
            {
                path = Path.Combine(id, path);
                var filename = Path.GetFileName(path);
                if (IsFileHidden(path))
                {
                    return RedirectToAction("Error", "Home");
                }

                var file = JekyllHandler.Domain.Query.GetHelpAdditionalFile(path);
                return File(file.data, file.type, filename, file.lastMod, EntityTagHeaderValue.Any);
            }
            catch (Exception ex)
            {
                JekyllHandler.Log.Error(ex, "Fehler bei Download von Datei {path} von {help}", path, id);
                return RedirectToAction("Error", "Home");
            }
        }

        [HttpPost("Challenges/{id}")]
        public IActionResult Challenges(string id, [FromBody] string path)
        {
            try
            {
                var filename = Path.GetFileName(path);
                if (IsFileHidden(path) || filename == "challenge.md")
                {
                    return Ok(new DownloadInfo(ErrorMessages.GenericError));
                }

                var file = JekyllHandler.Domain.Query.GetChallengeAdditionalFile(id, path);
                return Ok(new DownloadInfo(file.data));
            }
            catch (Exception ex)
            {
                JekyllHandler.Log.Error(ex, "Fehler bei Download von Datei {path} von {challenge}", path, id);
                return Ok(new DownloadInfo(ErrorMessages.GenericError));
            }
        }

        private static bool IsFileHidden(string path)
        {
            if (path.Contains(".."))
            {
                return false;
            }

            var entries = path.Split(@"\/", StringSplitOptions.RemoveEmptyEntries);
            return entries.Any(x => x.StartsWith("_"));
        }
    }
}
