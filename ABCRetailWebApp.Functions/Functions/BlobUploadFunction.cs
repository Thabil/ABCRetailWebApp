using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Files.Shares;
using System.Net;
using System.Text.Json;

namespace ABCRetailWebApp.Functions.Functions
{
    /// <summary>
    /// HTTP-triggered function that uploads a product image to Azure Blob Storage.
    ///
    /// The web app calls this with a 3-second timeout. If the function responds in time,
    /// the returned blobName is saved to the product record. If it times out, ImageBlobName
    /// stays null and the UI shows a placeholder — no error is surfaced to the admin.
    ///
    /// POST /api/BlobUploadFunction
    /// Body: multipart/form-data with fields:
    ///   - file      : the image binary
    ///   - productId : the product RowKey (used to build a deterministic blob name)
    ///   - contentType : MIME type (e.g. image/jpeg)
    /// Returns: 200 { "blobName": "product_{productId}.jpg" } or 400/500
    /// </summary>
    public class BlobUploadFunction
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ShareServiceClient _shareServiceClient;
        private readonly ILogger _logger;

        public BlobUploadFunction(
            BlobServiceClient blobServiceClient,
            ShareServiceClient shareServiceClient,
            ILoggerFactory loggerFactory)
        {
            _blobServiceClient = blobServiceClient;
            _shareServiceClient = shareServiceClient;
            _logger = loggerFactory.CreateLogger<BlobUploadFunction>();
        }

        [Function(nameof(BlobUploadFunction))]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
            FunctionContext context)
        {
            _logger.LogInformation("BlobUploadFunction triggered");

            try
            {
                // Parse multipart form
                var contentType = req.Headers.TryGetValues("Content-Type", out var ctValues)
                    ? ctValues.FirstOrDefault() ?? string.Empty
                    : string.Empty;

                if (!contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteStringAsync("Expected multipart/form-data");
                    return bad;
                }

                var form = await ParseMultipartAsync(req, contentType);

                if (form.FileBytes == null || form.FileBytes.Length == 0)
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteStringAsync("No file data received");
                    return bad;
                }

                if (string.IsNullOrEmpty(form.ProductId))
                {
                    var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                    await bad.WriteStringAsync("productId field is required");
                    return bad;
                }

                // Build deterministic blob name so the web app can predict it
                var extension = MimeToExtension(form.MimeType ?? "image/jpeg");
                var blobName = $"product_{form.ProductId}{extension}";

                // Upload to productimages container
                var container = _blobServiceClient.GetBlobContainerClient("productimages");
                await container.CreateIfNotExistsAsync(PublicAccessType.None);

                var blobClient = container.GetBlobClient(blobName);
                using var stream = new MemoryStream(form.FileBytes);
                var uploadOptions = new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = form.MimeType ?? "image/jpeg" } };
                await blobClient.UploadAsync(stream, uploadOptions);

                _logger.LogInformation("Blob uploaded: {BlobName} ({Bytes} bytes)", blobName, form.FileBytes.Length);

                // Append to file log
                await AppendToLogAsync($"product-events-{DateTime.UtcNow:yyyy-MM-dd}.log",
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] BLOB_UPLOADED | ProductId: {form.ProductId} | Blob: {blobName} | Size: {form.FileBytes.Length} bytes");

                var ok = req.CreateResponse(HttpStatusCode.OK);
                await ok.WriteAsJsonAsync(new { blobName });
                return ok;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in BlobUploadFunction: {Message}", ex.Message);

                // Log the failure — the web app will show a placeholder, not an error
                await AppendToLogAsync($"product-events-{DateTime.UtcNow:yyyy-MM-dd}.log",
                    $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] BLOB_UPLOAD_FAILED | Error: {ex.Message}");

                var error = req.CreateResponse(HttpStatusCode.InternalServerError);
                await error.WriteStringAsync($"Upload failed: {ex.Message}");
                return error;
            }
        }

        /// <summary>
        /// Minimal multipart parser — reads boundary, extracts file bytes and form fields.
        /// Avoids taking a dependency on an external multipart library.
        /// </summary>
        private static async Task<MultipartForm> ParseMultipartAsync(HttpRequestData req, string contentTypeHeader)
        {
            var form = new MultipartForm();

            // Extract boundary from Content-Type header
            var boundaryMarker = "boundary=";
            var boundaryIndex = contentTypeHeader.IndexOf(boundaryMarker, StringComparison.OrdinalIgnoreCase);
            if (boundaryIndex < 0) return form;
            var boundary = "--" + contentTypeHeader[(boundaryIndex + boundaryMarker.Length)..].Trim('"', ' ');

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            var parts = body.Split(boundary, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                if (part.TrimStart().StartsWith("--")) continue; // closing boundary

                // Split headers from body on double newline
                var headerBodySplit = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerBodySplit < 0) continue;

                var headers = part[..headerBodySplit];
                var value   = part[(headerBodySplit + 4)..].TrimEnd('\r', '\n', '-');

                if (headers.Contains("filename=", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract MIME type from Content-Type header in this part
                    var ctLine = headers.Split('\n')
                        .FirstOrDefault(l => l.TrimStart().StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
                    if (ctLine != null)
                        form.MimeType = ctLine.Split(':')[1].Trim();

                    // The value is the raw bytes encoded as a string — convert back
                    form.FileBytes = System.Text.Encoding.Latin1.GetBytes(value);
                }
                else if (headers.Contains("name=\"productId\"", StringComparison.OrdinalIgnoreCase))
                {
                    form.ProductId = value.Trim();
                }
                else if (headers.Contains("name=\"contentType\"", StringComparison.OrdinalIgnoreCase))
                {
                    form.MimeType = value.Trim();
                }
            }

            return form;
        }

        private static string MimeToExtension(string mimeType) => mimeType.ToLowerInvariant() switch
        {
            "image/png"  => ".png",
            "image/gif"  => ".gif",
            "image/webp" => ".webp",
            _            => ".jpg"
        };

        private async Task AppendToLogAsync(string fileName, string entry)
        {
            try
            {
                var share = _shareServiceClient.GetShareClient("applogs");
                await share.CreateIfNotExistsAsync();
                var dir = share.GetDirectoryClient("logs");
                await dir.CreateIfNotExistsAsync();
                var fileClient = dir.GetFileClient(fileName);
                var bytes = System.Text.Encoding.UTF8.GetBytes(entry + Environment.NewLine);

                if (await fileClient.ExistsAsync())
                {
                    var props = await fileClient.GetPropertiesAsync();
                    long existing = props.Value.ContentLength;
                    await fileClient.SetHttpHeadersAsync(existing + bytes.Length);
                    await fileClient.UploadRangeAsync(new Azure.HttpRange(existing, bytes.Length), new MemoryStream(bytes));
                }
                else
                {
                    await fileClient.CreateAsync(bytes.Length);
                    await fileClient.UploadRangeAsync(new Azure.HttpRange(0, bytes.Length), new MemoryStream(bytes));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Log append failed (non-fatal): {Message}", ex.Message);
            }
        }

        private class MultipartForm
        {
            public byte[]? FileBytes { get; set; }
            public string? ProductId { get; set; }
            public string? MimeType  { get; set; }
        }
    }
}
