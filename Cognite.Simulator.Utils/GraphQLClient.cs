using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Cognite.Extractor.Utils;
using Newtonsoft.Json.Linq;

namespace Cognite.Simulator.Utils {

    /// <summary>
    /// Simple GraphQL client for sending queries to a GraphQL endpoint.
    /// </summary>
    public class GraphQLClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _graphqlEndpoint;

        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _tenant;
        private readonly string _scope;
        private string _headerToken;

        /// <summary>
        /// Creates a new GraphQL client.
        /// </summary>
        public GraphQLClient(string graphqlEndpoint, CogniteConfig config)
        {
            _httpClient = new HttpClient();
            _graphqlEndpoint = graphqlEndpoint;
            _clientId = config.IdpAuthentication.ClientId;
            _clientSecret = config.IdpAuthentication.Secret;
            _tenant = config.IdpAuthentication.Tenant;
            _scope = config.IdpAuthentication.Scopes[0];

        }

        /// <summary>
        /// Gets an OAuth token from the API.
        /// </summary>
        public void GetOAuthTokenFromApi() {
            // Console.WriteLine($"Getting OAuth token from API");
            var authEndpoint = $"https://login.microsoftonline.com/{_tenant}/oauth2/v2.0/token";
            var request = new HttpRequestMessage(HttpMethod.Post, authEndpoint){
                    Content = new StringContent("", System.Text.Encoding.UTF8, "application/json")
                };
            try {
                // Console.WriteLine($"Getting OAuth token from {authEndpoint}");
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["scope"] = _scope
                });
                var response = _httpClient.SendAsync(request).Result;
                var responseContent = response.Content.ReadAsStringAsync().Result;
                var token = JObject.Parse(responseContent)["access_token"].ToString();
                _headerToken = token;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get OAuth token from {authEndpoint}", ex);
            }
            finally
            {
                // Make sure to dispose of the request object even in case of exceptions
                request.Dispose();
            }
        }

        /// <summary>
        /// Sends a GraphQL query to the endpoint.
        /// </summary>

        public async Task<JObject> SendQueryAsync(string query)
        {
            GetOAuthTokenFromApi();
            // var request = new HttpRequestMessage(HttpMethod.Post, _graphqlEndpoint);
            var request = new HttpRequestMessage{
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(_graphqlEndpoint),
                    Headers = {
                        // { "authority", "azure-dev.cognitedata.com" },
                        { "accept", "application/json, multipart/mixed" },
                        { "accept-language", "en-GB,en-US;q=0.9,en;q=0.8" },
                        { "authorization", "Bearer " + _headerToken },
                        { "cdf-version", "V20210406" },
                        { "user-agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.0.0 Safari/537.36" },
                        { "x-cdp-app", "fusion.cognite.com" },
                        { "x-cdp-sdk", "CogniteJavaScriptSDK:7.19.1" },
                    },
                    Content = new StringContent("{\"query\":\""+ query +"\",\"variables\":{}}", Encoding.UTF8, "application/json")
                };
            try
            {

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"GraphQL request failed with status code {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                return JObject.Parse(responseContent);
            }
            finally
            {
                request.Dispose();
            }
        }

        /// <summary>
        /// Gets a value from a JSON object using a path.
        /// </summary>
        public static object GetValueFromPath(JObject jsonObject, string path)
        {
            try
            {
                // Console.WriteLine($"Getting value from path {path}");
                // Console.WriteLine($"JSON object: {jsonObject}");
                JToken token = jsonObject.SelectToken(path);
                return token?.ToObject<object>();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get value from JSON path {path}", ex);
            }
        }
    }

}
