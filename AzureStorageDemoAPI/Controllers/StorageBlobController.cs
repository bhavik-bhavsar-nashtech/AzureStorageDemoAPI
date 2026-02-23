using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using AzureStorageDemoAPI.Models;
using Microsoft.AspNetCore.Mvc;

namespace AzureStorageDemoAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StorageBlobController : ControllerBase
    {
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<StorageBlobController> _logger;
        private readonly string _accountName;
        private readonly string _accountKey;

        public StorageBlobController(BlobServiceClient blobService, ILogger<StorageBlobController> logger, StorageConfiguration storageConfig)
        {
            blobService = blobService ?? throw new ArgumentNullException(nameof(blobService));
            _containerClient = blobService.GetBlobContainerClient("bbtestcontainer");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            storageConfig = storageConfig ?? throw new ArgumentNullException(nameof(storageConfig));

            // Use the connection string provided via DI (single source of truth)
            var connectionString = storageConfig.ConnectionString
                ?? throw new InvalidOperationException("Storage connection string not provided via DI.");

            if (!ExtractAccountCredentials(connectionString, out _accountName, out _accountKey))
                throw new InvalidOperationException("Unable to extract storage account credentials from connection string");
        }

        [HttpPost("generate-sas-token")]
        public IActionResult GenerateSasToken(
            [FromQuery] string blobName,
            [FromQuery] int expirationMinutes = 60,
            [FromQuery] bool includeRead = true,
            [FromQuery] bool includeWrite = false,
            [FromQuery] bool includeDelete = false)
        {
            if (string.IsNullOrWhiteSpace(blobName))
                return BadRequest("blobName is required.");

            try
            {
                var blobClient = _containerClient.GetBlobClient(blobName);

                // Build SAS permissions
                var permissions = new BlobSasPermissions();
                if (includeRead) permissions |= BlobSasPermissions.Read;
                if (includeWrite) permissions |= BlobSasPermissions.Write;
                if (includeDelete) permissions |= BlobSasPermissions.Delete;

                // Generate SAS URI using stored credentials
                var sasUri = blobClient.GenerateSasUri(
                    permissions,
                    DateTimeOffset.UtcNow.AddMinutes(expirationMinutes));

                _logger.LogInformation("Generated SAS token for blob '{BlobName}' with expiration {ExpirationMinutes} minutes", blobName, expirationMinutes);

                return Ok(new
                {
                    BlobName = blobName,
                    SasUri = sasUri.ToString(),
                    ExpirationMinutes = expirationMinutes,
                    Permissions = new { Read = includeRead, Write = includeWrite, Delete = includeDelete }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating SAS token for blob '{BlobName}'", blobName);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile(
            [FromQuery] string blobName,
            [FromQuery] string? sasToken = null,
            [FromHeader(Name = "Authorization")] string? authorizationHeader = null)
        {
            if (string.IsNullOrWhiteSpace(blobName))
                return BadRequest("blobName is required.");

            // Validate authentication
            if (!ValidateAuthentication(sasToken, authorizationHeader))
                return Unauthorized("Invalid or missing SAS token or Access token.");

            if (Request.ContentLength == 0)
                return BadRequest("File content is required.");

            try
            {
                var blobClient = _containerClient.GetBlobClient(blobName);
                await blobClient.UploadAsync(Request.Body, overwrite: true);

                _logger.LogInformation("File '{FileName}' uploaded successfully", blobName);

                return Ok(new
                {
                    BlobName = blobName,
                    Uri = blobClient.Uri.ToString(),
                    Uploaded = true
                });
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error uploading file '{FileName}'", blobName);
                return StatusCode((int)ex.Status, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error uploading file '{FileName}'", blobName);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("download")]
        public async Task<IActionResult> DownloadFile(
            [FromQuery] string blobName,
            [FromQuery] string? sasToken = null,
            [FromHeader(Name = "Authorization")] string? authorizationHeader = null)
        {
            if (string.IsNullOrWhiteSpace(blobName))
                return BadRequest("blobName is required.");

            // Validate authentication
            if (!ValidateAuthentication(sasToken, authorizationHeader))
                return Unauthorized("Invalid or missing SAS token or Access token.");

            try
            {
                var blobClient = _containerClient.GetBlobClient(blobName);
                var download = await blobClient.DownloadAsync();

                _logger.LogInformation("File '{FileName}' downloaded successfully", blobName);

                return File(download.Value.Content, "application/octet-stream", blobName);
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("File '{FileName}' not found", blobName);
                return NotFound($"Blob '{blobName}' not found.");
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error downloading file '{FileName}'", blobName);
                return StatusCode((int)ex.Status, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error downloading file '{FileName}'", blobName);
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("list")]
        public async Task<IActionResult> ListBlobs([FromQuery] string? prefix = null)
        {
            try
            {
                var blobs = new List<object>();
                await foreach (var blob in _containerClient.GetBlobsAsync(prefix: prefix))
                {
                    blobs.Add(new
                    {
                        Name = blob.Name,
                        Size = blob.Properties.ContentLength,
                        CreatedOn = blob.Properties.CreatedOn,
                        LastModified = blob.Properties.LastModified
                    });
                }

                _logger.LogInformation("Listed {Count} blobs with prefix '{Prefix}'", blobs.Count, prefix ?? "none");

                return Ok(blobs);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error listing blobs");
                return StatusCode((int)ex.Status, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error listing blobs");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("delete")]
        public async Task<IActionResult> DeleteFile(
            [FromQuery] string blobName,
            [FromQuery] string? sasToken = null,
            [FromHeader(Name = "Authorization")] string? authorizationHeader = null)
        {
            if (string.IsNullOrWhiteSpace(blobName))
                return BadRequest("blobName is required.");

            // Validate authentication
            if (!ValidateAuthentication(sasToken, authorizationHeader))
                return Unauthorized("Invalid or missing SAS token or Access token.");

            try
            {
                var blobClient = _containerClient.GetBlobClient(blobName);
                await blobClient.DeleteAsync();

                _logger.LogInformation("File '{FileName}' deleted successfully", blobName);

                return NoContent();
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                _logger.LogWarning("File '{FileName}' not found", blobName);
                return NotFound($"Blob '{blobName}' not found.");
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Error deleting file '{FileName}'", blobName);
                return StatusCode((int)ex.Status, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting file '{FileName}'", blobName);
                return StatusCode(500, ex.Message);
            }
        }

        /// <summary>
        /// Validates authentication using either SAS token or Access token (Bearer).
        /// SAS token can be passed as query parameter.
        /// Access token should be passed in Authorization header as "Bearer {token}".
        /// For this demo, Access token validation checks for "Bearer " prefix.
        /// In production, validate against Azure AD or your authentication system.
        /// </summary>
        private bool ValidateAuthentication(string? sasToken, string? authorizationHeader)
        {
            // Check for SAS token (query parameter)
            if (!string.IsNullOrWhiteSpace(sasToken))
            {
                // SAS token validation: In production, you might want to validate the token format
                // For now, we check if it has the typical SAS token pattern (contains 'sv=', 'sig=', etc.)
                if (sasToken.Contains("sv=") && sasToken.Contains("sig="))
                {
                    _logger.LogInformation("Request authenticated using SAS token");
                    return true;
                }
            }

            // Check for Bearer token (Access token in Authorization header)
            if (!string.IsNullOrWhiteSpace(authorizationHeader) && authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authorizationHeader.Substring("Bearer ".Length).Trim();
                if (!string.IsNullOrWhiteSpace(token))
                {
                    // In production, validate the token against Azure AD or your auth system
                    // For demo purposes, we just check that the token exists
                    _logger.LogInformation("Request authenticated using Access token");
                    return true;
                }
            }

            _logger.LogWarning("Request authentication failed: neither SAS token nor valid Access token provided");
            return false;
        }

        /// <summary>
        /// Extracts storage account name and key from connection string.
        /// </summary>
        private bool ExtractAccountCredentials(string? connectionString, out string accountName, out string accountKey)
        {
            accountName = string.Empty;
            accountKey = string.Empty;

            if (string.IsNullOrWhiteSpace(connectionString))
                return false;

            try
            {
                var parts = connectionString.Split(';');
                foreach (var part in parts)
                {
                    if (part.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase))
                        accountName = part.Substring("AccountName=".Length);
                    else if (part.StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase))
                        accountKey = part.Substring("AccountKey=".Length);
                }

                return !string.IsNullOrWhiteSpace(accountName) && !string.IsNullOrWhiteSpace(accountKey);
            }
            catch
            {
                return false;
            }
        }
    }
}
