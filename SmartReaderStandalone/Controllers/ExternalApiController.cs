#region copyright
//****************************************************************************************************
// Copyright ©2025 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using SmartReader.Infrastructure.Database;
using SmartReader.Infrastructure.ViewModel;
using SmartReaderStandalone.Authentication;
using SmartReaderStandalone.Utils;
using System.Net.Http.Headers;
using System.Text;

namespace SmartReaderStandalone.Controllers
{
    /// <summary>
    /// Controller for querying and publishing data to external APIs (product and order verification, publishing).
    /// </summary>
    [ApiController]
    [Route("api/external")]
    [AuthorizeBasicAuth]
    public class ExternalApiController : ControllerBase
    {
        private readonly RuntimeDb _db;
        private readonly ILogger<ExternalApiController> _logger;

        public ExternalApiController(RuntimeDb db, ILogger<ExternalApiController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Queries external API for a product by GTIN.
        /// </summary>
        /// <param name="gtin">The GTIN code.</param>
        /// <returns>External product verification result (JSON object or string).</returns>
        /// <response code="200">Returns the product verification result.</response>
        /// <response code="400">On API error.</response>
        [HttpGet("query/product/{gtin}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(string), 400)]
        public async Task<IActionResult> QueryProductByGtin([FromRoute] string gtin)
        {
            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && configDto.enableExternalApiVerification == "1")
            {
                var url = configDto.externalApiVerificationSearchProductUrl + gtin;
                using var httpClient = CreateHttpClient(url, configDto, out var handler);
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add(configDto.externalApiVerificationHttpHeaderName, configDto.externalApiVerificationHttpHeaderValue);

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(content))
                    return Ok(JToken.Parse(content));
                return Ok(content ?? "");
            }
            return Ok("");
        }

        /// <summary>
        /// Queries external API for order information (POST JSON body).
        /// </summary>
        /// <param name="jsonDocument">Order query payload.</param>
        /// <returns>External order verification result (JSON object or string).</returns>
        /// <response code="200">Returns the order verification result.</response>
        /// <response code="400">On API error.</response>
        [HttpPost("query/order")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(string), 400)]
        public async Task<IActionResult> QueryOrderByPayload([FromBody] object jsonDocument)
        {
            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && configDto.enableExternalApiVerification == "1")
            {
                var url = configDto.externalApiVerificationSearchOrderUrl;
                using var httpClient = CreateHttpClient(url, configDto, out var handler);

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonDocument.ToString() ?? "", Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add(configDto.externalApiVerificationHttpHeaderName, configDto.externalApiVerificationHttpHeaderValue);

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(content))
                    return Ok(JToken.Parse(content));
                return Ok(content ?? "");
            }
            return Ok("");
        }

        /// <summary>
        /// Queries external API for a specific order by order code.
        /// </summary>
        /// <param name="order">The order code or id.</param>
        /// <returns>External order verification result (JSON object or string).</returns>
        /// <response code="200">Returns the order verification result.</response>
        /// <response code="400">On API error.</response>
        [HttpGet("query/order/{order}")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(string), 400)]
        public async Task<IActionResult> QueryOrderByCode([FromRoute] string order)
        {
            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && configDto.enableExternalApiVerification == "1")
            {
                var url = configDto.externalApiVerificationSearchOrderUrl + order;
                using var httpClient = CreateHttpClient(url, configDto, out var handler);

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add(configDto.externalApiVerificationHttpHeaderName, configDto.externalApiVerificationHttpHeaderValue);

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(content))
                    return Ok(JToken.Parse(content));
                return Ok(content ?? "");
            }
            return Ok("");
        }

        /// <summary>
        /// Publishes data to an external API (POST).
        /// </summary>
        /// <param name="jsonDocument">Payload to publish.</param>
        /// <returns>External API publish result (JSON object or string).</returns>
        /// <response code="200">Returns the publish result.</response>
        /// <response code="400">On API error.</response>
        [HttpPost("publish")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(string), 400)]
        public async Task<IActionResult> PublishExternal([FromBody] object jsonDocument)
        {
            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && configDto.enableExternalApiVerification == "1")
            {
                var url = configDto.externalApiVerificationPublishDataUrl;
                using var httpClient = CreateHttpClient(url, configDto, out var handler);

                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonDocument.ToString() ?? "", Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add(configDto.externalApiVerificationHttpHeaderName, configDto.externalApiVerificationHttpHeaderValue);

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(content))
                    return Ok(JToken.Parse(content));
                return Ok(content ?? "");
            }
            return Ok("");
        }

        /// <summary>
        /// Publishes (PUT) data to an external API (used for updating status, etc).
        /// </summary>
        /// <param name="jsonDocument">Payload to update.</param>
        /// <returns>External API publish (PUT) result (JSON object or string).</returns>
        /// <response code="200">Returns the publish (PUT) result.</response>
        /// <response code="400">On API error.</response>
        [HttpPut("publish")]
        [ProducesResponseType(typeof(object), 200)]
        [ProducesResponseType(typeof(string), 400)]
        public async Task<IActionResult> PublishExternalPut([FromBody] object jsonDocument)
        {
            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && configDto.enableExternalApiVerification == "1")
            {
                var url = configDto.externalApiVerificationChangeOrderStatusUrl;
                if (url.Contains("/searches/results"))
                    url = url.Replace("/searches/results", "");
                using var httpClient = CreateHttpClient(url, configDto, out var handler);

                var request = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new StringContent(jsonDocument.ToString() ?? "", Encoding.UTF8, "application/json")
                };
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add(configDto.externalApiVerificationHttpHeaderName, configDto.externalApiVerificationHttpHeaderValue);

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(content))
                    return Ok(JToken.Parse(content));
                return Ok(content ?? "");
            }
            return Ok("");
        }

        /// <summary>
        /// Helper to create HttpClient with optional proxy and validation.
        /// </summary>
        private HttpClient CreateHttpClient(string url, StandaloneConfigDTO configDto, out HttpClientHandler handler)
        {
            handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            if (!string.IsNullOrEmpty(configDto.networkProxy))
            {
                var webProxy = new System.Net.WebProxy(configDto.networkProxy, int.Parse(configDto.networkProxyPort))
                {
                    Credentials = System.Net.CredentialCache.DefaultNetworkCredentials,
                    BypassProxyOnLocal = true
                };
                handler.Proxy = webProxy;
                handler.UseProxy = true;
            }
            return new HttpClient(handler) { BaseAddress = new Uri(url) };
        }
    }
}

