using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// HTTP client that can be used to download files from a 
    /// provided address and save it locally
    /// </summary>
    public class FileDownloadClient
    {
        private readonly HttpClient _client;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes this object with the given HTTP client and logger
        /// </summary>
        /// <param name="client">HTTP client</param>
        /// <param name="logger">logger</param>
        public FileDownloadClient(HttpClient client, ILogger<FileDownloadClient> logger)
        {
            _client = client;
            _logger = logger;
        }

        /// <summary>
        /// Downloads the file from the provided <paramref name="uri"/> and saves
        /// it in the provided <paramref name="filePath"/>
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="filePath"></param>
        /// <returns><c>true</c> if success, else <c>false</c></returns>
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
