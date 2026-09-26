using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ABCRetailWebApp.Services
{
    /// <summary>
    /// Thin wrapper around HttpClient for calling Azure Functions HTTP endpoints.
    /// - 3-second overall timeout per attempt
    /// - Up to 3 attempts with 500ms / 1s / 2s backoff on transient errors (5xx, network)
    /// - Never throws to the caller — returns null on all failures so the web app can fall back gracefully
    /// </summary>
    public class FunctionHttpClient
    {
        private readonly HttpClient _http;
        private readonly ILogger<FunctionHttpClient> _logger;
        private static readonly TimeSpan[] Backoff = [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

        public FunctionHttpClient(HttpClient http, ILogger<FunctionHttpClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        /// <summary>
        /// POST JSON to a function endpoint. Returns the parsed response or null on failure.
        /// </summary>
        public async Task<T?> PostJsonAsync<T>(string path, object payload) where T : class
        {
            var json = JsonSerializer.Serialize(payload);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _http.PostAsync(path, content, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }

                    _logger.LogWarning("Function {Path} returned {Status} on attempt {Attempt}", path, (int)response.StatusCode, attempt + 1);
                    if ((int)response.StatusCode < 500) break; // 4xx — don't retry
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Function {Path} timed out on attempt {Attempt}", path, attempt + 1);
                    break; // timeout — don't retry, return null immediately
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("Function {Path} network error on attempt {Attempt}: {Message}", path, attempt + 1, ex.Message);
                }

                if (attempt < 2)
                    await Task.Delay(Backoff[attempt]);
            }

            return null;
        }

        /// <summary>
        /// POST multipart/form-data to a function endpoint. Returns the parsed response or null on failure.
        /// </summary>
        public async Task<T?> PostMultipartAsync<T>(string path, MultipartFormDataContent form) where T : class
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var response = await _http.PostAsync(path, form, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        return JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }

                    _logger.LogWarning("Function {Path} returned {Status} on attempt {Attempt}", path, (int)response.StatusCode, attempt + 1);
                    if ((int)response.StatusCode < 500) break;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Function {Path} timed out on attempt {Attempt}", path, attempt + 1);
                    break;
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning("Function {Path} network error on attempt {Attempt}: {Message}", path, attempt + 1, ex.Message);
                }

                if (attempt < 2)
                    await Task.Delay(Backoff[attempt]);
            }

            return null;
        }
    }

    // Response shapes matching what the functions return
    public class TableWriteResponse  { public bool Success { get; set; } public string? RowKey { get; set; } }
    public class BlobUploadResponse  { public string? BlobName { get; set; } }
}
