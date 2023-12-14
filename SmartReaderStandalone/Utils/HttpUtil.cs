#region copyright
//****************************************************************************************************
// Copyright ©2023 Impinj, Inc.All rights reserved.              
//                                    
// You may use and modify this code under the terms of the Impinj Software Tools License & Disclaimer. 
// Visit https://support.impinj.com/hc/en-us/articles/360000468370-Software-Tools-License-Disclaimer   
// for full license details, or contact Impinj, Inc.at support@impinj.com for a copy of the license.   
//
//****************************************************************************************************
#endregion
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using RestSharp.Authenticators;
using Serilog;
using SmartReaderJobs.ViewModel.Antenna;
using SmartReaderJobs.ViewModel.Events;
using SmartReaderJobs.ViewModel.Mqtt;
using SmartReaderJobs.ViewModel.Reader;
using SmartReaderJobs.ViewModel.ReaderCommand;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace SmartReaderJobs.Utils;

public class HttpUtil
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _token;
    private readonly string _url;

    public HttpUtil(IHttpClientFactory httpClientFactory, string url, string token)
    {
        _httpClientFactory = httpClientFactory;
        _url = url;
        _token = token;
    }

    public async Task<List<SmartReaderCommandData>> GetCommandsAsync()
    {
        var filter = "{ \"filters\": [ [\"status\",\"=\",\"SOLICITADO\"] ]}";
        var responseData = new List<SmartReaderCommandData>();

        var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(_url + "/comandosleitores"),
            Content = new StringContent(filter, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        //.AddParameter("foo", "bar");
        var deserializedData = JsonConvert.DeserializeObject<SmartReaderCommand>(content);
        responseData = deserializedData.Data;


        return responseData;
    }

    public async Task<List<SmartReaderCommandData>> GetCommandsRunningByReaderAndTypeAsync(int readerId, string cmdType)
    {
        var filter = "{ \"filters\": [ [\"id_leitor\",\"=\"," + readerId +
                     "], [\"status\",\"=\",\"EXECUTANDO\"], [\"tipo_comando\",\"=\",\"" + cmdType + "\"] ]}";
        var responseData = new List<SmartReaderCommandData>();

        var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(_url + "/comandosleitores"),
            Content = new StringContent(filter, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var deserializedData = JsonConvert.DeserializeObject<SmartReaderCommand>(content);
        responseData = deserializedData.Data;

        return responseData;
    }

    public async Task<List<SmartReaderCommandData>> GetCommandsRunningByReaderAsync(int readerId)
    {
        var filter = "{ \"filters\": [ [\"id_leitor\",\"=\"," + readerId + "], [\"status\",\"=\",\"EXECUTANDO\"] ]}";
        var responseData = new List<SmartReaderCommandData>();

        var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(_url + "/comandosleitores"),
            Content = new StringContent(filter, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var deserializedData = JsonConvert.DeserializeObject<SmartReaderCommand>(content);
        responseData = deserializedData.Data;

        return responseData;
    }

    public async Task<List<SmartReaderCommandData>> GetCommandsByReaderAsync(int readerId)
    {
        var filter = "{ \"filters\": [ [\"id_leitor\",\"=\"," + readerId + "] ]}";
        var responseData = new List<SmartReaderCommandData>();

        var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(_url + "/comandosleitores"),
            Content = new StringContent(filter, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var deserializedData = JsonConvert.DeserializeObject<SmartReaderCommand>(content);
        responseData = deserializedData.Data;

        return responseData;
    }

    public async Task<bool> PostCommandsAsync(SmartReaderCommandData command)
    {
        var httpClient = _httpClientFactory.CreateClient();
        var param = JsonConvert.SerializeObject(command);
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri(_url + "/comandosleitores"),
            Content = new StringContent(param, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> PostStatusAsync(SmartReaderCommandData readerStatus)
    {
        var httpClient = _httpClientFactory.CreateClient();
        //httpClient.DefaultRequestHeaders.ExpectContinue = true;
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var param = JsonConvert.SerializeObject(readerStatus);
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri(_url + "/statusleitores"),
            Content = new StringContent(param, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return response.IsSuccessStatusCode;
    }

    public async Task<bool> PostTagEventToSmartReaderServerAsync(SmartReaderTagEventData tagEventData)
    {
        var httpClient = _httpClientFactory.CreateClient();
        //httpClient.DefaultRequestHeaders.ExpectContinue = true;
        httpClient.Timeout = TimeSpan.FromSeconds(3);
        var param = JsonConvert.SerializeObject(tagEventData);
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri(_url + "/leituras"),
            Content = new StringContent(param, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        return response.IsSuccessStatusCode;
    }

    public async Task<SmartReaderSetupData> GetLeitorAsync(int readerId)
    {
        List<SmartReaderSetupData> responseData = null;

        var filter = "{ \"filters\": [ [\"id\",\"=\"," + readerId + "] ]}";

        var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(_url + "/leitores"),
            Content = new StringContent(filter, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var deserializedData = JsonConvert.DeserializeObject<SmartReaderSetup>(content);
        responseData = deserializedData.Data;

        if (responseData != null && responseData.Any()) return responseData.FirstOrDefault();

        return null;
    }

    public async Task<SmartReaderSetupData> GetLeitorBySerialAsync(string serial)
    {
        List<SmartReaderSetupData> responseData = null;

        var filter = "{ \"filters\": [ [\"serial\",\"=\"," + serial + "] ]}";

        var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(_url + "/leitores"),
            Content = new StringContent(filter, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var deserializedData = JsonConvert.DeserializeObject<SmartReaderSetup>(content);
        responseData = deserializedData.Data;

        if (responseData != null && responseData.Any()) return responseData.FirstOrDefault();

        return null;
    }

    public async Task<List<SmartReaderAntennaSetupListData>> GetAntenasAsync(int readerId)
    {
        var filter = "{ \"filters\": [ [\"id_leitor\",\"=\"," + readerId + "] ]}";

        var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(_url + "/antenas"),
            Content = new StringContent(filter, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var deserializedData = JsonConvert.DeserializeObject<SmartReaderAntennaSetup>(content);
        var responseData = deserializedData.Data;

        return responseData;
    }

    public async Task<List<SmartReaderMqttData>> GetMqttAsync(int readerId)
    {
        var filter = "{ \"filters\": [ [\"id_leitor\",\"=\"," + readerId + "] ]}";

        var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Get,
            RequestUri = new Uri(_url + "/mqtt"),
            Content = new StringContent(filter, Encoding.UTF8,
                MediaTypeNames.Application.Json /* or "application/json" in older versions */)
        };
        //request.Headers.Add("Accept", "application/json");
        //request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("Authorization", _token);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        var deserializedData = JsonConvert.DeserializeObject<SmartReaderMqtt>(content);
        var responseData = deserializedData.Data;

        return responseData;
    }

    public async Task<string> GetBearerTokenUsingJsonBodyAsync(string url, string jsonBody)
    {
        string token = null;
        //var hostName = "http://coala-app01-api.elisbrasil.com";//ApplicationConfiguration.Get("http://coala-app01-api.elisbrasil.com");
        //var urlToken = hostName + "/api/Login/Authenticate";
        //var bodyRequest = "{\"login\": \"inventario\",\"password\": \"inventario\"}";

        try
        {
            var fullUriData = new Uri(url);
            var host = fullUriData.Host;
            var baseUri = fullUriData.GetLeftPart(UriPartial.Authority);
            var rightActionPath = url.Replace(baseUri, "");
            var httpClient = _httpClientFactory.CreateClient();


            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(url),
                Content = new StringContent(jsonBody, Encoding.UTF8,
                    "application/json" /* or "application/json" in older versions */)
            };
            request.Headers.Add("Accept", "application/json");


            httpClient.DefaultRequestHeaders
                .Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            //request.Headers.Add("Accept", "*/*");
            //request.Headers.Add("Connection", "keep-alive");
            //request.Headers.Add("Content-Type", "application/json");


            //httpClient.BaseAddress = new Uri(baseUri);
            Log.Debug(url);
            Log.Debug(jsonBody);
            Console.WriteLine(jsonBody);

            var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            //var response = await httpClient.PostAsJsonAsync(fullUriData, request).ConfigureAwait(false);
            //response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Log.Debug(content);
            Console.WriteLine(content);

            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result))
            {
                var tokenResult = response.Content.ReadAsStringAsync().Result;
                var tokenJson = JObject.Parse(tokenResult);
                if (tokenJson.ContainsKey("token")) token = tokenJson.GetValue("token").ToString();
            }

            return token;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error sending data");
            throw;
        }

        //var uri = new Uri(url);
        //var host = uri.Host;
        //var leftActionPath = uri.GetLeftPart(UriPartial.Authority);
        //var rightActionPath = url.Replace(leftActionPath, "");

        //var client = new RestSharp.RestClient(leftActionPath);
        //var request = new RestRequest(rightActionPath, Method.Post);
        //request.AddHeader("Accept", "*/*");
        //request.AddHeader("Content-Type", "application/json");
        //request.AddHeader("cache-control", "no-cache");

        ////var body = @"{""keys"":[""303B03568348D70000000001""]}";
        ////string body = JsonConvert.SerializeObject(bodyRequest);
        ////request.RequestFormat = DataFormat.Json;
        //JObject json = JObject.Parse(jsonBody);
        //request.AddParameter("application/json", json, ParameterType.RequestBody, false);
        ////request.AddJsonBody(bodyRequest);

        //Console.WriteLine(request.ToString());

        //if (url.Contains("https"))
        //{
        //    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        //}

        //var response = client.ExecuteAsync(request);

        ////var request = new RestSharp.RestRequest()
        ////{
        ////    RequestFormat = RestSharp.DataFormat.Json
        ////};
        //////request.AddJsonBody(bodyRequest);
        ////JObject json = JObject.Parse(jsonBody);
        ////request.AddBody(json);
        ////request.AddHeader("Content-Type", "application/json");
        //////request.AddHeader("Authorization", "Bearer " + User.IdToken());
        ////if (url.Contains("https"))
        ////{
        ////    System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
        ////}
        ////var response = await client.ExecutePostAsync(request);

        //if (response.Result.IsSuccessful && !string.IsNullOrEmpty(response.Result.Content))
        //{
        //    token = response.Result.Content;
        //}

        return token;
    }

    public async Task<string?> GetDataAsync(string url, string basicAuthUsername, string basicAuthPassword,
        string bearerToken)
    {
        var client = new RestClient(url);

        var request = new RestRequest();

        request.AddHeader("Accept", "application/json");
        if (!string.IsNullOrEmpty(basicAuthUsername) && !string.IsNullOrEmpty(basicAuthPassword))
            client.Authenticator = new HttpBasicAuthenticator(basicAuthUsername, basicAuthPassword);
        else if (!string.IsNullOrEmpty(bearerToken)) request.AddHeader("Authorization", "Bearer " + bearerToken);

        var response = await client.ExecuteGetAsync(request);

        if (response.IsSuccessful) return response.Content;

        return null;
    }

    public async Task<bool> PostJsonListBodyDataAsync(string url, object bodyRequest, string basicAuthUsername,
        string basicAuthPassword, string bearerToken, bool checkResult, string actionPath)
    {
        var success = true;
        //var hostName = "http://coala-app01-api.elisbrasil.com";//ApplicationConfiguration.Get("http://coala-app01-api.elisbrasil.com");
        //var urlToken = hostName + "/api2/Sync/PostClientEventTagPortal";
        //var bodyRequest = "{\"login\": \"inventario\",\"password\": \"inventario\"}";

        var client = new RestClient(url);

        //var request = new RestSharp.RestRequest();
        var request = new RestRequest(actionPath, Method.Post);
        //{
        //    RequestFormat = RestSharp.DataFormat.Json
        //};
        var json = JsonConvert.SerializeObject(bodyRequest);
        request.AddParameter("application/json; charset=utf-8", json, ParameterType.RequestBody);
        request.RequestFormat = DataFormat.Json;
        //request.AddHeader("Content-Type", "application/json");
        request.AddHeader("Accept", "*/*");

        //request.AddBody(bodyRequest);


        if (!string.IsNullOrEmpty(basicAuthUsername) && !string.IsNullOrEmpty(basicAuthPassword))
            client.Authenticator = new HttpBasicAuthenticator(basicAuthUsername, basicAuthPassword);
        else if (!string.IsNullOrEmpty(bearerToken)) request.AddHeader("Authorization", "Bearer " + bearerToken);

        var response = await client.ExecuteAsync(request);

        if (checkResult) success = response.IsSuccessful;

        return success;
    }

    public async Task<string> PostJsonObjectDataAsync(string url, object bodyRequest, string basicAuthUsername,
        string basicAuthPassword, string bearerToken, bool checkResult, string actionPath, string? authHeader = null,
        string? authHeaderValue = null)
    {
        var returnedData = "";
        try
        {

            var httpClient = _httpClientFactory.CreateClient();

            //httpClient.DefaultRequestHeaders.ExpectContinue = true;
            httpClient.Timeout = TimeSpan.FromSeconds(3);

            var param = JsonConvert.SerializeObject(bodyRequest);
            var completeUrl = "";

            //new StringContent(param, Encoding.UTF8, "application/json" /* or "application/json" in older versions */),
            //JsonContent.Create(bodyRequest),
            var uri = new Uri(url);
            var baseUri = uri.GetLeftPart(UriPartial.Authority);
            var rightUriData = url.Replace(baseUri, "");

            if (string.IsNullOrEmpty(actionPath))
                actionPath = rightUriData;
            else
                actionPath = rightUriData + actionPath;

            var fullUriData = baseUri + actionPath;

            var baseUriObj = new Uri(baseUri);
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(baseUriObj, actionPath),
                Content = new StringContent(param, Encoding.UTF8,
                    "application/json" /* or "application/json" in older versions */)
            };
            request.Headers.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(authHeader) && !string.IsNullOrEmpty(authHeaderValue))
                request.Headers.Add(authHeader, authHeaderValue);


            httpClient.DefaultRequestHeaders
                .Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            //request.Headers.Add("Accept", "*/*");
            //request.Headers.Add("Connection", "keep-alive");
            //request.Headers.Add("Content-Type", "application/json");


            //httpClient.BaseAddress = new Uri(baseUri);
            Log.Debug(completeUrl);
            Log.Debug(param);
            Console.WriteLine(param);
            //request.Headers.Add("Accept", "application/json");
            //request.Headers.Add("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(basicAuthUsername) && !string.IsNullOrEmpty(basicAuthPassword))
            {
                var authToken = Encoding.ASCII.GetBytes($"{basicAuthUsername}:{basicAuthPassword}");
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));
                //request.Headers.Add("Authorization", _token);
            }
            else if (!string.IsNullOrEmpty(bearerToken))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                //request.Headers.Add("Authorization", _token);
                //request.AddHeader("Authorization", "Bearer " + bearerToken);
            }
            //if(actionPath.StartsWith("/"))
            //{
            //    actionPath= actionPath.Remove(0,1);
            //}

            var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            //var response = await httpClient.PostAsJsonAsync(fullUriData, request).ConfigureAwait(false);
            //response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Log.Debug(content);
            Console.WriteLine(content);
            var statusCode = (int)response.StatusCode;

            returnedData = statusCode + " - " + content;
            //if (checkResult)
            //    returnedData = response.StatusCode + " - " + content;
            //else
            //    returnedData = content;

            Log.Debug("returnedData: " + returnedData);

        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error sending data");
            Console.WriteLine("Unexpected error sending data - " + ex.Message);
            returnedData = "500 - Error";
            //throw;
        }

        return returnedData;
    }

    public async Task<string> PostJsonObjectDataRestSharpAsync(string url, object bodyRequest, string basicAuthUsername,
        string basicAuthPassword, string bearerToken, bool checkResult, string actionPath)
    {
        var returnedData = "";
        try
        {
            var uri = new Uri(url);
            var baseUri = uri.GetLeftPart(UriPartial.Authority);
            var rightUriData = url.Replace(baseUri, "");

            if (string.IsNullOrEmpty(actionPath))
                actionPath = rightUriData;
            else
                actionPath = rightUriData + actionPath;

            var fullUriData = baseUri + actionPath;

            var restClient = new RestClient(baseUri);

            if (!string.IsNullOrEmpty(basicAuthUsername) && !string.IsNullOrEmpty(basicAuthPassword))
                restClient.Authenticator = new HttpBasicAuthenticator(basicAuthUsername, basicAuthPassword);
            var request = new RestRequest(actionPath, Method.Post);
            request.AddHeader("Accept", "*/*");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("cache-control", "no-cache");

            //var body = @"{""keys"":[""303B03568348D70000000001""]}";
            var body = JsonConvert.SerializeObject(bodyRequest);
            //request.RequestFormat = DataFormat.Json;
            request.AddParameter("application/json", body, ParameterType.RequestBody, false);
            //request.AddJsonBody(bodyRequest);

            Console.WriteLine(request.ToString());

            var response = restClient.ExecuteAsync(request);
            Console.WriteLine(response.Result.Content);

            var content = response.Result.Content;
            Log.Debug(content);
            Console.WriteLine(content);
            if (checkResult)
                returnedData = response.Result.StatusCode + " - " + content;
            else
                returnedData = content;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error sending data");
            throw;
        }

        return returnedData;
    }

    public async Task DownloadFileAsync(string remoteUrl, string localFile)
    {
        var httpClient = new HttpClient();

        using (var stream = await httpClient.GetStreamAsync(remoteUrl))
        {
            using (var fileStream = new FileStream(localFile, FileMode.CreateNew))
            {
                await stream.CopyToAsync(fileStream);
            }
        }
    }

    public async Task UploadFileAsync(string readerAddress, string localFile, string basicAuthUsername, string basicAuthPassword)
    {


        var httpClientHandler = new HttpClientHandler
        {


            ServerCertificateCustomValidationCallback =
                   (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                       chain, errors) => true)
        };

        //var httpClient = new HttpClient(httpClientHandler);

        try
        {
            using (HttpClient httpClient = new HttpClient(httpClientHandler))
            {
                if (!string.IsNullOrEmpty(basicAuthUsername) && !string.IsNullOrEmpty(basicAuthPassword))
                {
                    var authToken = Encoding.ASCII.GetBytes($"{basicAuthUsername}:{basicAuthPassword}");
                    httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));
                    //request.Headers.Add("Authorization", _token);
                }

                using (MultipartFormDataContent formData = new MultipartFormDataContent())
                {
                    // Create the file stream content
                    //FileStreamContent fileContent = new FileStreamContent(File.OpenRead(localFile));
                    var filestream = File.OpenRead(localFile);
                    var fileContent = new StreamContent(filestream);
                    //fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    //fileContent.Headers.ContentType = new MediaTypeHeaderValue("multipart/form-data");


                    var localFilename = Path.GetFileName(localFile);
                    // Add the file content to the form data
                    formData.Add(fileContent, "upgradeFile", localFilename);

                    var url = "https://" + readerAddress + "/api/v1/system/image/upgrade";
                    httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                    httpClient.Timeout = TimeSpan.FromSeconds(120);
                    // Send the POST request
                    HttpResponseMessage response = await httpClient.PostAsync(url, formData);

                    // Handle the response
                    if (response.IsSuccessStatusCode)
                    {
                        Log.Information("File uploaded successfully!");
                    }
                    else
                    {
                        Log.Information($"Error uploading file. StatusCode: {response.StatusCode}");
                    }
                }
            }

            //var request = new HttpRequestMessage(HttpMethod.Post, "https://" + readerAddress + "/api/v1/system/image/upgrade");
            //request.Headers.Add("Accept", "application/json");
            ////request.Headers.Add("Content-Type", "multipart/form-data");

            //if (!string.IsNullOrEmpty(basicAuthUsername) && !string.IsNullOrEmpty(basicAuthPassword))
            //{
            //    var authToken = Encoding.ASCII.GetBytes($"{basicAuthUsername}:{basicAuthPassword}");
            //    httpClient.DefaultRequestHeaders.Authorization =
            //        new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));
            //    //request.Headers.Add("Authorization", _token);
            //}
            ////request.Headers.Add("Authorization", "Basic cm9vdDppbXBpbmo=");
            //var content = new MultipartFormDataContent();
            //content.Add(new StreamContent(File.OpenRead(localFile)), "upgradeFile", localFile);
            //request.Content = content;
            //var response = await httpClient.SendAsync(request);
            //response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error uploading image.");
            throw;
        }


    }

    public string ExternalApiAuthenticateAsync(string url, string data)
    {
        var returnedData = "";
        try
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            var content = new StringContent(data, null, "application/json");
            request.Content = content;
            var response = client.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            returnedData = response.Content.ReadAsStringAsync().Result;
        }
        catch (Exception)
        {

            throw;
        }

        return returnedData;
    }

    public string ExternalApiSearchOrderAsync(string url, string token, string data)
    {
        var returnedData = "";
        try
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", token);
            var content = new StringContent(data, null, "application/json");
            request.Content = content;
            var response = client.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            returnedData = response.Content.ReadAsStringAsync().Result;
        }
        catch (Exception)
        {

            throw;
        }

        return returnedData;
    }

    public string ExternalApiOrderPublishDataAsync(string url, string token, string data)
    {
        var returnedData = "";
        try
        {
            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Put, url);
            request.Headers.Add("Authorization", token);
            var content = new StringContent(data, null, "application/json");
            request.Content = content;
            var response = client.SendAsync(request).Result;
            response.EnsureSuccessStatusCode();
            returnedData = response.Content.ReadAsStringAsync().Result;
        }
        catch (Exception)
        {

            throw;
        }

        return returnedData;
    }
}