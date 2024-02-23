using Serilog.Core;
using Serilog.Events;
using Serilog.Context;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Cognite.Simulator.Utils {

    public class ScopedRemoteApiSink : ILogEventSink
    {
        private readonly string apiUrl;
        private readonly List<string> logBuffer = new List<string>();

        public ScopedRemoteApiSink(string apiUrl)
        {
            this.apiUrl = apiUrl;
        }

        public void Emit(LogEvent logEvent)
        {
            // Customize the log data to send to the remote API
            var logData = new
            {
                Timestamp = logEvent.Timestamp,
                Level = logEvent.Level,
                Message = logEvent.RenderMessage(),
                Properties = logEvent.Properties,
                // Add more properties as needed
            };

            // Convert log data to JSON
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(logData);

            // Store log data in the buffer
            logBuffer.Add(json);
        }

        public void Flush()
        {
            // Send the collected logs to the remote API
            SendToRemoteApi(logBuffer).Wait(); // Wait for the request to complete

            // Clear the log buffer
            logBuffer.Clear();
        }

        private async Task SendToRemoteApi(List<string> logs)
        {
            // use the Cognite Client here
            using (var httpClient = new HttpClient())
            {
                Console.WriteLine($"Sending {logs.Count} logs to {apiUrl}");
                // var content = new StringContent($"[{string.Join(",", logs)}]", Encoding.UTF8, "application/json");
                // await httpClient.PostAsync(apiUrl, content);
            }
        }
    }
}
