using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils
{
    /// <summary>
    /// HTTP client that can be used to download and upload files from/to a server
    /// </summary>
    public class FileStorageClient
    {
        private readonly HttpClient _client;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes this object with the given HTTP client and logger
        /// </summary>
        /// <param name="client">HTTP client</param>
        /// <param name="logger">logger</param>
        public FileStorageClient(HttpClient client, ILogger<FileStorageClient> logger)
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

        /// <summary>
        /// Uploads a file to the provided <paramref name="uri"/>
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="fileStream"></param>
        /// <returns></returns>
        /// <exception cref="HttpRequestException"></exception>
        public async Task UploadFileAsync(Uri uri, StreamContent fileStream) {
            if (fileStream == null) {
                throw new ArgumentNullException(nameof(fileStream));
            }

            fileStream.Headers.Add("Content-Type", "application/octet-stream");

            var putResponse = await _client.PutAsync(uri, fileStream).ConfigureAwait(false);

            putResponse.EnsureSuccessStatusCode();
            if (putResponse.StatusCode >= System.Net.HttpStatusCode.BadRequest) {
                throw new HttpRequestException(
                    $"Did not get a success HTTP response on file upload: {putResponse.StatusCode} url: {uri}"
                );
            }
        }
    }
}
