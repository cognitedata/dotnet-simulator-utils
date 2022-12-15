using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    public class FileDownloadClient
    {
        private readonly HttpClient _client;
        private readonly ILogger _logger;

        public FileDownloadClient(HttpClient client, ILogger<FileDownloadClient> logger)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<bool> DownloadFileAsync(Uri uri, string filePath)
        {
            try
            {
                var response = await _client.GetAsync(uri)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to download the file: {Message}", response.ReasonPhrase);
                    return false;
                }

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                using (var fs = new FileStream(filePath, FileMode.CreateNew))
                {
                    await response.Content.CopyToAsync(fs)
                        .ConfigureAwait(false);
                    return true;
                }
            }
            catch (HttpRequestException e)
            {
                // File cannot be downloaded, skip for now and try again later
                _logger.LogError("Failed to download the file: {Message}", e.Message);
                return false;
            }
        }
    }
}
