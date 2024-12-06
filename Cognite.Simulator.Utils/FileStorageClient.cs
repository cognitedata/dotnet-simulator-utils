﻿using Microsoft.Extensions.Logging;
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

        private static readonly int DefaultBufferSize = 81920; // 80 KB
        private static readonly long MaxFileDownloadSize = 8 * 1024 * 1024 * 1024L; // 8 GB
        private static readonly long LargeFileSize = 512 * 1024 * 1024L; // 512 MB

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
        /// <param name="uri">URI to download from</param>
        /// <param name="filePath">Path to save the file</param>
        /// <param name="failLargeFiles">Set to true to throw an exception if the file is larger than <see cref="LargeFileSize"/></param>
        /// <returns><c>true</c> if success, else <c>false</c></returns>
        public async Task<bool> DownloadFileAsync(Uri uri, string filePath, bool failLargeFiles = false)
        {
            try
            {
                var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to download the file: {Message}", response.ReasonPhrase);
                    return false;
                }

                if (response.Content.Headers.ContentLength > MaxFileDownloadSize)
                {
                    _logger.LogWarning("File size exceeds the maximum allowed size: {Size} bytes", MaxFileDownloadSize);
                    return false;
                }

                if (failLargeFiles && response.Content.Headers.ContentLength > LargeFileSize)
                {
                    throw new ConnectorException(
                        $"The file is larger than {LargeFileSize / 1024 / 1024} MB and needs to be pre-downloaded before any operations can be performed." +
                        "Connector hasn't been able to pre-download the file yet. " +
                        "Please wait for the file to be downloaded and try again."
                    );
                }

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                using (var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: DefaultBufferSize, useAsync: true))
                {
                    var buffer = new byte[DefaultBufferSize];
                    int bytesRead;

                    while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                    }
                    return true;
                }
            }
            catch (HttpRequestException e)
            {
                // File cannot be downloaded, skip for now and try again later
                _logger.LogError("Failed to download the file {uri}: {Message}", uri, e.Message);
            }
            catch (IOException e)
            {
                _logger.LogError("I/O error occurred while saving the file into {filePath}: {Message}", filePath, e.Message);
            }
            return false;
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
