using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using QuizAPI.Services;

namespace QuizAPI.Controllers
{
    [ApiController]
    [Route("api/import")]
    public class ImportController : ControllerBase
    {
        private readonly QuizImportService _import;
        private readonly ILogger<ImportController> _logger;

        public ImportController(QuizImportService import, ILogger<ImportController> logger)
        {
            _import = import;
            _logger = logger;
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        public async Task<IActionResult> Upload([FromForm] FileUploadModel model)
        {
            var msg = await _import.SaveUploadAsync(model.File);
            _logger.LogInformation("Admin uploaded file {FileName}", model.File?.FileName);
            return Ok(msg);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("upload-package")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> UploadPackage([FromForm] FileUploadModel model)
        {
            var result = await _import.SaveUploadPackageAsync(model.File);
            _logger.LogInformation("Admin uploaded package {FileName}", model.File?.FileName);
            return Ok(result);
        }

        //[Authorize(Roles ="Admin")]
        [Authorize(Roles = "Admin")]
        [HttpPost("process/{fileName}")]
        public async Task<IActionResult> Process(string fileName)
        {
            try
            {
                var result = await _import.ProcessCsvAsync(fileName);
                _logger.LogInformation("Admin processed import file {FileName}", fileName);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import processing failed for file {FileName}", fileName);
                return Problem("Import processing failed. Review the import file format and server logs, then try again.");
            }
        }





        [Authorize(Roles = "Admin")]
[HttpGet("history")]
public IActionResult History([FromQuery] int take = 4)
{
    if (take < 1) take = 1;
    if (take > 4) take = 4;
    return Ok(_import.ReadImportHistory(take));
}

    }

    // define the upload model right here so Swagger can describe it
    public class FileUploadModel
    {
        [FromForm(Name = "File")]
        public IFormFile File { get; set; } = default!;
    }
}
