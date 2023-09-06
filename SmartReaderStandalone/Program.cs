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
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using MQTTnet.Server;
using MQTTnet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Events;
using SmartReader.Infrastructure.Database;
using SmartReader.Infrastructure.ViewModel;
using SmartReader.IotDeviceInterface;
using SmartReaderJobs.Utils;
using SmartReaderJobs.ViewModel.Mqtt.Endpoint;
using SmartReaderStandalone.Authentication;
using SmartReaderStandalone.Entities;
using SmartReaderStandalone.Services;
using SmartReaderStandalone.Utils;
using SmartReaderStandalone.ViewModel;
using SmartReaderStandalone.ViewModel.Status;
using Endpoint = SmartReaderJobs.ViewModel.Mqtt.Endpoint.Endpoint;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using JsonSerializer = System.Text.Json.JsonSerializer;
using System.IO;
using System.Runtime.Loader;

if(File.Exists("/customer/upgrading"))
{
    Environment.Exit(0);
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

//var configuration = new ConfigurationBuilder()
//    .AddJsonFile("appsettings.json")
//    .Build();

//// Get the current build configuration
//string buildConfiguration = "Release"; // Default to Debug if unable to determine
//#if DEBUG
//    buildConfiguration = "Debug";
//#endif


//// Get the value based on the build configuration
//if("Debug".Equals(buildConfiguration))
//{
//    var debugAddress = configuration.GetValue<string>("ReaderInfo:DebugAddress");
//    string mySettingValue = configuration["ReaderInfo:Address"] = debugAddress;

//}


var builder = WebApplication.CreateBuilder(args);

//builder.Host.UseSerilog((ctx, lc) => lc
//        .WriteTo.Console()
//        .ReadFrom.Configuration(ctx.Configuration));

builder.Host.UseSerilog((ctx, lc) => lc
    .WriteTo.Console()
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Information()
    .WriteTo.File("/customer/wwwroot/logs/log.txt", LogEventLevel.Debug,
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        fileSizeLimitBytes: 102400, rollOnFileSizeLimit: true, retainedFileCountLimit: 2));


//builder.Services.AddLogging();
// Register the Serilog logger with DI
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddSerilog();
});

builder.Services.AddControllers();
// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddHttpClient();
//builder.Services.AddHttpClient("smartreaderHttpClient", client =>
//{
//    client.BaseAddress = new Uri("https://localhost");
//});
//builder.Services.Configure<HttpClientFactoryOptions>("smartreaderHttpClient", options =>
//{
//    options.HttpClientHandler = new HttpClientHandler
//    {
//        Proxy = new WebProxy("http://proxy.example.com:8888") // Replace with your proxy URL and port
//    };
//});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
//builder.Services.AddCors();
builder.Services.AddCors(o => o.AddPolicy("AllowAll", builder =>
{
    builder.AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader();
}));

builder.Services.AddScoped<IUserService, UserService>();

builder.Services.AddDbContext<RuntimeDb>(opt => opt.UseInMemoryDatabase("RuntimeDb"));

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddDirectoryBrowser();

builder.WebHost.ConfigureKestrel(opt =>
{
    opt.ListenAnyIP(8443, listOpt => { listOpt.UseHttps(@"/customer/localhost.pfx", "r700"); });
});
//builder.Services.AddRouting();
//builder.Services.AddHostedService<SummaryQueueBackgroundService>();
//builder.Services.AddHostedService<IotInterfaceService>();
builder.Services.AddSingleton<IotInterfaceService>();
builder.Services.AddSingleton<IHostedService, IotInterfaceService>(serviceProvider => serviceProvider.GetService<IotInterfaceService>());
//builder.Services.AddScoped<IIotInterfaceService, IotInterfaceService>();
builder.Services.AddScoped<ISummaryQueueBackgroundService, SummaryQueueBackgroundService>();

// Create and start the MQTT servers
var mqttFactory = new MqttFactory();
MqttServer tcpMqttServer = null;
try
{
    var configDto = ConfigFileHelper.ReadFile();
    if (configDto != null 
        && "127.0.0.1".Equals(configDto.mqttBrokerAddress))
    {
        // Configure the MQTT server options for TCP
        var tcpMqttServerOptions = new MqttServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointPort(1883) // Set the MQTT port for TCP
            .Build();

        tcpMqttServer = mqttFactory.CreateMqttServer(tcpMqttServerOptions);

        await tcpMqttServer.StartAsync();
    }
}
catch (Exception)
{

    
}

var app = builder.Build();

//ILogger logger = app.Services.GetService<ILogger<Program>>();
var logger = app.Services.GetRequiredService<ILogger<Program>>();

var readerAddress = app.Configuration["ReaderInfo:Address"] ?? "127.0.0.1";
// Get the current build configuration
string buildConfiguration = "Release"; // Default to Debug if unable to determine
#if DEBUG
buildConfiguration = "Debug";
#endif
// Get the value based on the build configuration
if ("Debug".Equals(buildConfiguration))
{
    readerAddress = app.Configuration["ReaderInfo:DebugAddress"] ?? readerAddress;
}

var rshellAuthUserName = app.Configuration["RShellAuth:UserName"] ?? "root";

var rshellAuthPassword = app.Configuration["RShellAuth:Password"] ?? "impinj";

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}


//app.UseCors(x => x
//        .AllowAnyOrigin()
//        .AllowAnyMethod()
//        .AllowAnyHeader());

// custom basic auth middleware

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<BasicAuthMiddleware>();


app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.Context.Items["BasicAuth"] is not true)
        {
            // respond HTTP 401 Unauthorized.
            ctx.Context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            ctx.Context.Response.ContentLength = 0;
            ctx.Context.Response.Body = Stream.Null;
            ctx.Context.Response.Headers.Add("WWW-Authenticate", string.Format("Basic realm=\"{0}\"", "R700"));
            //ctx.Context.Response.Redirect("/");
        }
    }
});

app.UseFileServer(true);

if (Directory.Exists("/customer/wwwroot/logs"))
    app.UseFileServer(new FileServerOptions
    {
        FileProvider = new PhysicalFileProvider("/customer/wwwroot/logs"),
        RequestPath = "/logs",
        EnableDirectoryBrowsing = true
    });


//app.MapGet("/api/stream/volumes", async (RuntimeDb db, HttpContext context) =>
//{

//    async IAsyncEnumerable<List<JsonDocument>> StreamSmartReaderSkuSummaryModelAsync()
//    {

//        var resp = context.Response;
//        resp.Headers.ContentType = "text/event-stream";
//        var keepaliveStopWatch = new Stopwatch();
//        keepaliveStopWatch.Start();
//        //var serializer = new JsonSerializer();
//        while (true)
//        {
//            //JObject returnedData;
//            var dataModel = db.SmartReaderSkuSummaryModels.LastOrDefault();
//            if (dataModel != null && !string.IsNullOrEmpty(dataModel.Value))
//            {

//                var json = dataModel.Value;

//                var jsonOject = JsonDocument.Parse(json);
//                //JObject jsonOject = JObject.Parse(json);

//                var jsonString = JsonSerializer.Serialize(jsonOject);
//                var returnedData = Regex.Unescape(jsonString);

//                if (returnedData.StartsWith("[")) returnedData = returnedData.Substring(1);

//                db.SmartReaderSkuSummaryModels.Remove(dataModel);
//                await db.SaveChangesAsync();

//                List<JsonDocument> result = new();
//                result.Add(jsonOject);

//                yield return result;

//            }
//            else if (keepaliveStopWatch.IsRunning && keepaliveStopWatch.Elapsed.TotalSeconds > 10)
//            {
//                keepaliveStopWatch.Restart();
//                var jsonOject = JsonDocument.Parse(@"{}");
//                List<JsonDocument> result = new();
//                result.Add(jsonOject);
//                yield return result;
//            }

//            await Task.Delay(100);
//        }
//    }

//    return StreamSmartReaderSkuSummaryModelAsync();
//});

app.MapGet("/api/stream/volumes", async (RuntimeDb db, HttpContext context) =>
{
    var producerService = context.RequestServices.GetRequiredService<ISummaryQueueBackgroundService>();


    async IAsyncEnumerable<List<JsonDocument>> StreamSmartReaderSkuSummaryModelAsync()
    {

        var resp = context.Response;
        resp.Headers.ContentType = "text/event-stream";
        var keepaliveStopWatch = new Stopwatch();
        keepaliveStopWatch.Start();
        //var serializer = new JsonSerializer();
        while (true)
        {
            string dataModel = null;
            if (producerService.HasDataAvailable())
            {
                dataModel = producerService.GetData();
                if (!string.IsNullOrEmpty(dataModel))
                {
                    var json = dataModel;

                    var jsonOject = JsonDocument.Parse(json);
                    //JObject jsonOject = JObject.Parse(json);

                    var jsonString = JsonSerializer.Serialize(jsonOject);
                    var returnedData = Regex.Unescape(jsonString);

                    if (returnedData.StartsWith("[")) returnedData = returnedData.Substring(1);

                    List<JsonDocument> result = new();
                    result.Add(jsonOject);

                    yield return result;
                }

            }
            else if (keepaliveStopWatch.IsRunning && keepaliveStopWatch.Elapsed.TotalSeconds > 10)
            {
                keepaliveStopWatch.Restart();
                var jsonOject = JsonDocument.Parse(@"{}");
                List<JsonDocument> result = new();
                result.Add(jsonOject);
                yield return result;
            }

            await Task.Delay(100);
        }
    }

    return StreamSmartReaderSkuSummaryModelAsync();



});

app.MapGet("/api/stream/tags", async (RuntimeDb db, HttpContext context) =>
{

    async IAsyncEnumerable<List<JsonDocument>> StreamSmartReaderTagReadModelAsync()
    {

        var resp = context.Response;
        resp.Headers.ContentType = "text/event-stream";

        while (true)
        {
            //JObject returnedData;
            var dataModel = db.SmartReaderTagReadModels.LastOrDefault();
            if (dataModel != null && !string.IsNullOrEmpty(dataModel.Value))
            {

                var json = dataModel.Value;
                logger.LogInformation("Publishing data: " + json);

                var jsonOject = JsonDocument.Parse(json);

                var jsonString = JsonSerializer.Serialize(jsonOject);
                var returnedData = Regex.Unescape(jsonString);

                if (returnedData.StartsWith("[")) returnedData = returnedData.Substring(1);

                db.SmartReaderTagReadModels.Remove(dataModel);
                await db.SaveChangesAsync();

                List<JsonDocument> result = new();
                result.Add(jsonOject);

                yield return result;

            }

            await Task.Delay(100);
        }
    }

    return StreamSmartReaderTagReadModelAsync();
});
//RequireAuth
//app.MapGet("/api/settings", [Authorize] async (RuntimeDb db) =>
app.MapGet("/api/settings", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {
        var dtos = new List<StandaloneConfigDTO>();


        var configDto = ConfigFileHelper.ReadFile();

        var configModel = db.ReaderConfigs.FindAsync("READER_CONFIG").Result;
        if (configModel != null && !string.IsNullOrEmpty(configModel.Value))
        {
            var storedSettingsDto = JsonConvert.DeserializeObject<StandaloneConfigDTO>(configModel.Value);
            if (storedSettingsDto != null)
            {
                storedSettingsDto = StandaloneConfigDTO.CleanupUrlEncoding(storedSettingsDto);
                dtos.Add(storedSettingsDto);
                return Results.Ok(dtos);
            }

            if (configDto != null && !string.IsNullOrEmpty(configDto.readerName))
            {
                dtos.Add(configDto);
                return Results.Ok(dtos);
            }

            return Results.NotFound();
        }

        if (configDto != null && !string.IsNullOrEmpty(configDto.readerName))
        {
            dtos.Add(configDto);
            return Results.Ok(dtos);
        }

        return Results.NotFound();
    }
    catch (Exception)
    {
        return Results.NotFound();
    }
});

app.MapPost("/api/settings", [AuthorizeBasicAuth] async ([FromBody] StandaloneConfigDTO config, RuntimeDb db) =>
{

    try
    {

        try
        {
            if (!string.IsNullOrEmpty(config.licenseKey))
            {
                var configLicenseModel = db.ReaderConfigs.FindAsync("READER_LICENSE").Result;
                if (configLicenseModel == null)
                {
                    configLicenseModel = new ReaderConfigs();
                    configLicenseModel.Id = "READER_LICENSE";
                    configLicenseModel.Value = config.licenseKey;
                    db.ReaderConfigs.Add(configLicenseModel);
                }
                else
                {
                    configLicenseModel.Value = config.licenseKey;
                    db.ReaderConfigs.Update(configLicenseModel);
                }

                db.SaveChangesAsync();
            }
        }
        catch (Exception exDb)
        {
            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
        }

        try
        {
            if (config != null)
            {
                if ("1".Equals(config.advancedGpoEnabled))
                {
                    if ("6".Equals(config.advancedGpoMode1)
                    || "6".Equals(config.advancedGpoMode2)
                    || "6".Equals(config.advancedGpoMode3))
                    {
                        config.softwareFilterEnabled = "1";
                        if ("0".Equals(config.softwareFilterField))
                        {
                            config.softwareFilterField = "1";
                        }
                    }
                }

                if ("1".Equals(config.tagPresenceTimeoutEnabled))
                {
                    config.softwareFilterField = config.tagPresenceTimeoutInSec;
                }
            }

            var configModel = db.ReaderConfigs.FindAsync("READER_CONFIG").Result;
            if (configModel == null)
            {
                configModel = new ReaderConfigs();
                configModel.Id = "READER_CONFIG";
                configModel.Value = JsonConvert.SerializeObject(config);
                db.ReaderConfigs.Add(configModel);
            }
            else
            {
                configModel.Value = JsonConvert.SerializeObject(config);
                db.ReaderConfigs.Update(configModel);
            }

            db.SaveChangesAsync();
            Console.WriteLine(config.licenseKey);
            Console.WriteLine(config.session);
            logger.LogInformation("Settings saved.");
        }
        catch (Exception exDb1)
        {
            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb1.Message);
        }

        try
        {
            if (config != null)
                if (string.Equals("1", config.systemDisableImageFallbackStatus, StringComparison.OrdinalIgnoreCase))
                {
                    if (!File.Exists("/customer/disable-fallback"))
                    {
                        File.WriteAllText("/customer/disable-fallback", "ok");
                        logger.LogInformation("Requesting image fallback to be disabled. ");
                        var rshell = new RShellUtil(readerAddress, rshellAuthUserName, rshellAuthPassword);
                        try
                        {
                            var resultDisableImageFallback = rshell.SendCommand("config image disablefallback");
                            var lines = resultDisableImageFallback.Split("\n");
                            foreach (var line in lines)
                            {
                                logger.LogInformation(line);
                            }
                            var resultDisableImageFallbackReboot = rshell.SendCommand("reboot");
                        }
                        catch (Exception)
                        {
                        }
                    }

                }
                else if (string.Equals("0", config.systemDisableImageFallbackStatus, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists("/customer/disable-fallback"))
                    {
                        File.Delete("/customer/disable-fallback");
                    }
                }
        }
        catch (Exception exFallback)
        {
            logger.LogError(exFallback, "unexpected error disabling fallback.");
        }


        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.NotFound();
    }
});
/*
app.MapPost("/api/rshell", [AuthorizeBasicAuth] async ([FromBody] JsonDocument jsonDocument, RuntimeDb db) =>
{
    string result = "";
    Dictionary<string, string> keyValuePairs = new Dictionary<string, string>();

    try
    {

        var jsonDocumentStr = "";
        using (var stream = new MemoryStream())
        {
            var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            jsonDocument.WriteTo(writer);
            writer.Flush();
            jsonDocumentStr = Encoding.UTF8.GetString(stream.ToArray());
        }

        if (logger != null)
        {
            logger.LogInformation("jsonDocumentStr -> ");
            logger.LogInformation(jsonDocumentStr);
        }

        JsonElement usernameNode = jsonDocument.RootElement.GetProperty("username");
        var rshellUsername = usernameNode.GetString();
        logger.LogInformation($"rshellUsername: {rshellUsername}");
        JsonElement passwordNode = jsonDocument.RootElement.GetProperty("password");
        var rshellPassword = passwordNode.GetString();
        logger.LogInformation($"rshellPassword: {rshellPassword}");
        JsonElement commandNode = jsonDocument.RootElement.GetProperty("command");
        var rshellCommand = commandNode.GetString();
        logger.LogInformation($"rshellCommand: {rshellCommand}");

        try
        {
            if (!string.IsNullOrEmpty(rshellCommand))
            {
                var rshell = new RShellUtil(readerAddress, rshellUsername, rshellPassword);
                var tempResult = rshell.SendCommand(rshellCommand);
                rshell.Disconnect();
                logger.LogInformation($"rshellResult: {tempResult}");
                var lines = tempResult.Split("\n");
                
                foreach (var line in lines)
                {
                    var values = line.Split("=");
                    keyValuePairs.Add(values[0], values[1]);
                }

                


            }
        }
        catch (Exception exDb)
        {
            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
        }


        return Results.Ok(keyValuePairs);
    }
    catch (Exception)
    {
        return Results.NotFound();
    }
});
*/

app.MapGet("/api/query/external/product/{gtin}", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, string gtin, RuntimeDb db) =>
    {
        var requestResult = "";

        try
        {
            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && !string.IsNullOrEmpty(configDto.enableExternalApiVerification)
                                  && "1".Equals(configDto.enableExternalApiVerification))
            {
                var url = configDto.externalApiVerificationSearchProductUrl + gtin;
                var fullUriData = new Uri(url);
                var host = fullUriData.Host;
                var baseUri = fullUriData.GetLeftPart(UriPartial.Authority);
                var rightActionPath = url.Replace(baseUri, "");
                var httpClientHandler = new HttpClientHandler
                {


                    ServerCertificateCustomValidationCallback =
                   (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                       chain, errors) => true)
                };

                HttpClient httpClient = new()
                {
                    BaseAddress = new Uri(url)
                };

                if (!string.IsNullOrEmpty(configDto.networkProxy))
                {
                    WebProxy webProxy = new WebProxy(configDto.networkProxy, int.Parse(configDto.networkProxyPort));
                    webProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                    webProxy.BypassProxyOnLocal = true;
                    webProxy.BypassList.Append("169.254.1.1");
                    webProxy.BypassList.Append(readerAddress);
                    webProxy.BypassList.Append("localhost");

                    httpClientHandler = new HttpClientHandler
                    {

                        Proxy = webProxy,
                        UseProxy = true,
                        ServerCertificateCustomValidationCallback =
                        (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                            chain, errors) => true)
                    };
                    httpClient = new HttpClient(httpClientHandler)
                    {
                        BaseAddress = new Uri(url)
                    };

                }

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Get,
                    RequestUri = new Uri(url)
                };
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add(configDto.externalApiVerificationHttpHeaderName,
                    configDto.externalApiVerificationHttpHeaderValue);
                ;


                httpClient.DefaultRequestHeaders
                    .Accept
                    .Add(new MediaTypeWithQualityHeaderValue("application/json"));

                logger.LogInformation(url);

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                logger.LogInformation(content);
                logger.LogInformation(content);

                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result))
                {
                    requestResult = response.Content.ReadAsStringAsync().Result;
                    Log.Debug(requestResult);
                    return Results.Ok(JsonDocument.Parse(requestResult));
                }
            }

            return Results.Ok(requestResult);
        }
        catch (Exception exDb)
        {
            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
            return Results.BadRequest(requestResult);
        }
    });

app.MapPost("/api/query/external/order", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db) =>
    {
        var requestResult = "";

        try
        {
            var jsonDocumentStr = "";
            using (var stream = new MemoryStream())
            {
                var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                jsonDocument.WriteTo(writer);
                writer.Flush();
                jsonDocumentStr = Encoding.UTF8.GetString(stream.ToArray());
            }

            if (logger != null)
            {
                logger.LogInformation("jsonDocumentStr -> ");
                logger.LogInformation(jsonDocumentStr);
            }

            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && !string.IsNullOrEmpty(configDto.enableExternalApiVerification)
                                  && "1".Equals(configDto.enableExternalApiVerification))
            {
                if (logger != null)
                    logger.LogInformation("enableExternalApiVerification -> " +
                                          configDto.enableExternalApiVerification);
                var url = configDto.externalApiVerificationSearchOrderUrl;
                var fullUriData = new Uri(url);
                var host = fullUriData.Host;
                var baseUri = fullUriData.GetLeftPart(UriPartial.Authority);
                var rightActionPath = url.Replace(baseUri, "");
                var httpClientHandler = new HttpClientHandler
                {


                    ServerCertificateCustomValidationCallback =
                   (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                       chain, errors) => true)
                };
                HttpClient httpClient = new()
                {
                    BaseAddress = new Uri(url)
                };

                if (!string.IsNullOrEmpty(configDto.networkProxy))
                {
                    WebProxy webProxy = new WebProxy(configDto.networkProxy, int.Parse(configDto.networkProxyPort));
                    webProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                    webProxy.BypassProxyOnLocal = true;
                    webProxy.BypassList.Append("169.254.1.1");
                    webProxy.BypassList.Append(readerAddress);
                    webProxy.BypassList.Append("localhost");

                    httpClientHandler = new HttpClientHandler
                    {

                        Proxy = webProxy,
                        UseProxy = true,
                        ServerCertificateCustomValidationCallback =
                        (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                            chain, errors) => true)
                    };
                    httpClient = new HttpClient(httpClientHandler)
                    {
                        BaseAddress = new Uri(url)
                    };

                }


                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(url),
                    Content = new StringContent(jsonDocumentStr, Encoding.UTF8,
                        "application/json" /* or "application/json" in older versions */)
                };
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add(configDto.externalApiVerificationHttpHeaderName,
                    configDto.externalApiVerificationHttpHeaderValue);
                ;


                httpClient.DefaultRequestHeaders
                    .Accept
                    .Add(new MediaTypeWithQualityHeaderValue("application/json"));

                if (logger != null)
                {
                    logger.LogInformation(url);
                    logger.LogInformation(jsonDocumentStr);
                }
                //Console.WriteLine(jsonDocument);

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                //Log.Debug(content);
                if (logger != null) logger.LogInformation(content);
                logger.LogInformation(content);

                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result))
                {
                    requestResult = response.Content.ReadAsStringAsync().Result;
                    //Log.Debug(requestResult);
                    if (logger != null) logger.LogInformation(requestResult);

                    return Results.Ok(JsonDocument.Parse(requestResult));
                }
            }
            else
            {
                if (logger != null) logger.LogError("enableExternalApiVerification disabled or null ");
            }

            return Results.Ok(requestResult);
        }
        catch (Exception exDb)
        {
            if (logger != null) logger.LogError(exDb, "Error processing order request.");
            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
            return Results.BadRequest(requestResult);
        }
    });

app.MapGet("/api/query/external/order/{order}", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, string order, RuntimeDb db) =>
{
    var requestResult = "";

    try
    {


        var configDto = ConfigFileHelper.ReadFile();
        if (configDto != null && !string.IsNullOrEmpty(configDto.enableExternalApiVerification)
                              && "1".Equals(configDto.enableExternalApiVerification))
        {
            if (logger != null)
                logger.LogInformation("enableExternalApiVerification -> " +
                                      configDto.enableExternalApiVerification);
            var url = configDto.externalApiVerificationSearchOrderUrl + order;
            var fullUriData = new Uri(url);
            var host = fullUriData.Host;
            var baseUri = fullUriData.GetLeftPart(UriPartial.Authority);
            var rightActionPath = url.Replace(baseUri, "");
            var httpClientHandler = new HttpClientHandler
            {


                ServerCertificateCustomValidationCallback =
               (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                   chain, errors) => true)
            };

            HttpClient httpClient = new()
            {
                BaseAddress = new Uri(url)
            };

            if (!string.IsNullOrEmpty(configDto.networkProxy))
            {
                WebProxy webProxy = new WebProxy(configDto.networkProxy, int.Parse(configDto.networkProxyPort));
                webProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                webProxy.BypassProxyOnLocal = true;
                webProxy.BypassList.Append("169.254.1.1");
                webProxy.BypassList.Append(readerAddress);
                webProxy.BypassList.Append("localhost");

                httpClientHandler = new HttpClientHandler
                {

                    Proxy = webProxy,
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback =
                    (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                        chain, errors) => true)
                };

                httpClient = new HttpClient(httpClientHandler)
                {
                    BaseAddress = new Uri(url)
                };

            }

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(url),
            };
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add(configDto.externalApiVerificationHttpHeaderName,
                configDto.externalApiVerificationHttpHeaderValue);
            ;


            httpClient.DefaultRequestHeaders
                .Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (logger != null)
            {
                logger.LogInformation(url);
                //logger.LogInformation(jsonDocumentStr);
            }
            //Console.WriteLine(jsonDocument);

            var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            //Log.Debug(content);
            if (logger != null) logger.LogInformation(content);
            Console.WriteLine(content);

            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result))
            {
                requestResult = response.Content.ReadAsStringAsync().Result;
                //Log.Debug(requestResult);
                if (logger != null) logger.LogInformation(requestResult);

                return Results.Ok(JsonDocument.Parse(requestResult));
            }
        }
        else
        {
            if (logger != null) logger.LogError("enableExternalApiVerification disabled or null ");
        }

        return Results.Ok(requestResult);
    }
    catch (Exception exDb)
    {
        if (logger != null) logger.LogError(exDb, "Error processing order request.");
        File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
        return Results.BadRequest(requestResult);
    }
});

app.MapPost("/api/publish/external", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db) =>
    {
        var requestResult = "";

        try
        {
            var jsonDocumentStr = "";
            using (var stream = new MemoryStream())
            {
                var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
                jsonDocument.WriteTo(writer);
                writer.Flush();
                jsonDocumentStr = Encoding.UTF8.GetString(stream.ToArray());
            }

            var configDto = ConfigFileHelper.ReadFile();
            if (configDto != null && !string.IsNullOrEmpty(configDto.enableExternalApiVerification)
                                  && "1".Equals(configDto.enableExternalApiVerification))
            {
                var url = configDto.externalApiVerificationPublishDataUrl;
                var fullUriData = new Uri(url);
                var host = fullUriData.Host;
                var baseUri = fullUriData.GetLeftPart(UriPartial.Authority);
                var rightActionPath = url.Replace(baseUri, "");
                var httpClientHandler = new HttpClientHandler
                {


                    ServerCertificateCustomValidationCallback =
                   (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                       chain, errors) => true)
                };
                HttpClient httpClient = new()
                {
                    BaseAddress = new Uri(url)
                };

                if (!string.IsNullOrEmpty(configDto.networkProxy))
                {
                    WebProxy webProxy = new WebProxy(configDto.networkProxy, int.Parse(configDto.networkProxyPort));
                    webProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                    webProxy.BypassProxyOnLocal = true;
                    webProxy.BypassList.Append("169.254.1.1");
                    webProxy.BypassList.Append(readerAddress);
                    webProxy.BypassList.Append("localhost");

                    httpClientHandler = new HttpClientHandler
                    {

                        Proxy = webProxy,
                        UseProxy = true,
                        ServerCertificateCustomValidationCallback =
                        (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                            chain, errors) => true)
                    };
                    httpClient = new HttpClient(httpClientHandler)
                    {
                        BaseAddress = new Uri(url)
                    };

                }

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(url),
                    Content = new StringContent(jsonDocumentStr, Encoding.UTF8,
                        "application/json" /* or "application/json" in older versions */)
                };
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add(configDto.externalApiVerificationHttpHeaderName,
                    configDto.externalApiVerificationHttpHeaderValue);
                ;


                httpClient.DefaultRequestHeaders
                    .Accept
                    .Add(new MediaTypeWithQualityHeaderValue("application/json"));

                Log.Debug(url);
                Log.Debug(jsonDocumentStr);

                var response = await httpClient.SendAsync(request).ConfigureAwait(false);
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                Log.Debug(content);
                Console.WriteLine(content);

                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result))
                {
                    requestResult = response.Content.ReadAsStringAsync().Result;
                    Log.Debug(requestResult);
                    return Results.Ok(JsonDocument.Parse(requestResult));
                }
            }

            return Results.Ok(requestResult);
        }
        catch (Exception exDb)
        {
            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
            return Results.BadRequest(requestResult);
        }
    });

app.MapPut("/api/publish/external", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db) =>
{
    var requestResult = "";

    try
    {
        var jsonDocumentStr = "";
        using (var stream = new MemoryStream())
        {
            var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            jsonDocument.WriteTo(writer);
            writer.Flush();
            jsonDocumentStr = Encoding.UTF8.GetString(stream.ToArray());
        }

        var configDto = ConfigFileHelper.ReadFile();
        if (configDto != null && !string.IsNullOrEmpty(configDto.enableExternalApiVerification)
                              && "1".Equals(configDto.enableExternalApiVerification))
        {

            var url = configDto.externalApiVerificationChangeOrderStatusUrl;
            if (url.Contains("/searches/results"))
            {
                url = url.Replace("/searches/results", "");
            }
            var fullUriData = new Uri(url);
            var host = fullUriData.Host;
            var baseUri = fullUriData.GetLeftPart(UriPartial.Authority);
            var rightActionPath = url.Replace(baseUri, "");
            var httpClientHandler = new HttpClientHandler
            {


                ServerCertificateCustomValidationCallback =
                   (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                       chain, errors) => true)
            };
            HttpClient httpClient = new()
            {
                BaseAddress = new Uri(url)
            };

            if (!string.IsNullOrEmpty(configDto.networkProxy))
            {
                WebProxy webProxy = new WebProxy(configDto.networkProxy, int.Parse(configDto.networkProxyPort));
                webProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                webProxy.BypassProxyOnLocal = true;
                webProxy.BypassList.Append("169.254.1.1");
                webProxy.BypassList.Append(readerAddress);
                webProxy.BypassList.Append("localhost");

                httpClientHandler = new HttpClientHandler
                {

                    Proxy = webProxy,
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback =
                    (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                        chain, errors) => true)
                };
                httpClient = new HttpClient(httpClientHandler)
                {
                    BaseAddress = new Uri(url)
                };

            }

            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Put,
                RequestUri = new Uri(url),
                Content = new StringContent(jsonDocumentStr, Encoding.UTF8,
                    "application/json" /* or "application/json" in older versions */)
            };
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add(configDto.externalApiVerificationHttpHeaderName,
                configDto.externalApiVerificationHttpHeaderValue);
            ;


            httpClient.DefaultRequestHeaders
                .Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            logger.LogDebug(url);
            logger.LogDebug(jsonDocumentStr);

            var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            logger.LogDebug(content);
            Console.WriteLine(content);

            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result))
            {
                requestResult = response.Content.ReadAsStringAsync().Result;
                logger.LogDebug(requestResult);
                return Results.Ok(JsonDocument.Parse(requestResult));
            }
        }

        return Results.Ok(requestResult);
    }
    catch (Exception exDb)
    {
        File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
        return Results.BadRequest(requestResult);
    }
});


app.MapGet("/api/getserial", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    var serial = db.ReaderStatus.FindAsync("READER_SERIAL");

    if (serial != null && serial.Result != null && serial.Result.Value != null)
    {

        var json = JsonConvert.DeserializeObject<List<SmartreaderSerialNumberDto>>(serial.Result.Value);
        return Results.Ok(json);
    }

    return Results.NotFound();
});

app.MapGet("/api/deviceid", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    var configDto = ConfigFileHelper.ReadFile();
    if (configDto != null && !string.IsNullOrEmpty(configDto.readerName))
    {
        var json = new Dictionary<object, object>();
        json.Add("readerName", configDto.readerName);
        return Results.Ok(json);
    }

    return Results.NotFound();
});

app.MapGet("/api/getstatus", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    var status = db.ReaderStatus.FindAsync("READER_STATUS");

    if (status != null && status.Result != null)
    {
        var json = JsonConvert.DeserializeObject<List<SmartreaderRunningStatusDto>>(status.Result.Value);
        return Results.Ok(json);
    }

    return Results.NotFound();
});

app.MapGet("/api/start-preset", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {
        var command = new ReaderCommands();
        command.Id = "START_PRESET";
        command.Value = "START";
        command.Timestamp = DateTime.Now;
        db.ReaderCommands.Add(command);
        db.SaveChangesAsync();
        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/start-inventory", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {
        var command = new ReaderCommands();
        command.Id = "START_INVENTORY";
        command.Value = "START";
        command.Timestamp = DateTime.Now;
        db.ReaderCommands.Add(command);
        db.SaveChangesAsync();
        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/stop-preset", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {
        var command = new ReaderCommands();
        command.Id = "STOP_PRESET";
        command.Value = "STOP";
        command.Timestamp = DateTime.Now;
        db.ReaderCommands.Add(command);
        db.SaveChangesAsync();
        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/stop-inventory", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {
        var command = new ReaderCommands();
        command.Id = "STOP_INVENTORY";
        command.Value = "STOP";
        command.Timestamp = DateTime.Now;
        db.ReaderCommands.Add(command);
        db.SaveChangesAsync();
        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/upgrade-firmware", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {
        var configDto = ConfigFileHelper.ReadFile();
        if (configDto != null && !string.IsNullOrEmpty(configDto.systemImageUpgradeUrl))
        {
            var command = new ReaderCommands();
            command.Id = "UPGRADE_SYSTEM_IMAGE";
            command.Value = configDto.systemImageUpgradeUrl;
            command.Timestamp = DateTime.Now;
            db.ReaderCommands.Add(command);
            db.SaveChangesAsync();
        }

        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/gpo/{port}/status/{status}", [AuthorizeBasicAuth] async (int port, string status, RuntimeDb db) =>
{
    try
    {
        var statusToSet = false;
        if ("ON".Equals(status.ToUpper()))
            statusToSet = true;
        else if ("TRUE".Equals(status.ToUpper()))
            statusToSet = true;
        else if ("1".Equals(status)) statusToSet = true;


        var command = new ReaderCommands();
        command.Id = "SET_GPO_" + port;
        command.Value = "" + statusToSet;
        command.Timestamp = DateTime.Now;
        db.ReaderCommands.Add(command);
        db.SaveChangesAsync();
        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.Problem();
    }
});

app.MapGet("/api/filter/clean", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {

        var command = new ReaderCommands();
        command.Id = "CLEAN_EPC_SOFTWARE_HISTORY_FILTERS";
        command.Value = "ALL";
        command.Timestamp = DateTime.Now;
        db.ReaderCommands.Add(command);
        db.SaveChangesAsync();
        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.Problem();
    }
});

app.MapGet("/api/reload", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {
        try
        {
            //request a start command
            var command = new ReaderCommands();
            command.Id = "START_INVENTORY";
            command.Value = "START";
            command.Timestamp = DateTime.Now;
            db.ReaderCommands.Add(command);
            await db.SaveChangesAsync();
            await Task.Delay(100);
        }
        catch (Exception)
        {
        }

        // exits the app
        //await Task.Delay(TimeSpan.FromSeconds(2));
        logger.LogInformation("Restarting process");
        //// Restart the application by spawning a new process with the same arguments
        //var process = Process.GetCurrentProcess();
        //process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
        //Process.Start(process.MainModule.FileName);
        Environment.Exit(0);

        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.Ok();
    }
});

app.MapGet("/api/getcapabilities", async (RuntimeDb db) =>
{
    List<SmartReaderCapabilities> capabilities = new List<SmartReaderCapabilities>();



    using (IR700IotReader _iotDeviceInterfaceClient = new R700IotReader(readerAddress, "", true, true, rshellAuthUserName, rshellAuthPassword))
    {
        //IR700IotReader _iotDeviceInterfaceClient = new R700IotReader(readerAddress, "", true, true, rshellAuthUserName, rshellAuthUserName);
        var systemInfo = _iotDeviceInterfaceClient.GetSystemInfoAsync().Result;
        var systemRegion = _iotDeviceInterfaceClient.GetSystemRegionInfoAsync().Result;
        var systemPower = _iotDeviceInterfaceClient.GetSystemPowerAsync().Result;
        bool isPoePlus = false;
        if (systemPower.PowerSource.Equals(Impinj.Atlas.PowerSource.Poeplus))
        {
            isPoePlus = true;
        }
        else
        {
            var rshell = new RShellUtil(readerAddress, rshellAuthUserName, rshellAuthPassword);
            try
            {
                var resultRfidStat = rshell.SendCommand("show system power");
                rshell.Disconnect();
                var lines = resultRfidStat.Split("\n");
                foreach (var line in lines)
                {
                    if (line.StartsWith("PowerSource"))
                    {
                        if (line.Contains("PoE+") || line.Contains("poe+"))
                        {
                            isPoePlus = true;
                        }
                        else
                        {
                            isPoePlus = false;
                        }
                        break;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        var capability = new SmartReaderCapabilities();
        capability.RxTable = Utils.GetDefaultRxTable();
        capability.TxTable = Utils.GetDefaultTxTable(systemInfo.ProductModel, isPoePlus, systemRegion.OperatingRegion);
        capability.RfModeTable = Utils.GetDefaultRfModeTable();
        capability.MaxAntennas = 4;
        capability.SearchModeTable = Utils.GetDefaultSearchModeTable();
        capability.LicenseValid = 1;
        capability.ValidAntennas = "1,2,3,4";
        capability.ModelName = "R700";
        try
        {
            var configModel = db.ReaderConfigs.FindAsync("READER_CONFIG").Result;
            if (configModel != null && !string.IsNullOrEmpty(configModel.Value))
            {
                var storedSettingsDto = JsonConvert.DeserializeObject<StandaloneConfigDTO>(configModel.Value);
                if (storedSettingsDto != null)
                {
                    storedSettingsDto = StandaloneConfigDTO.CleanupUrlEncoding(storedSettingsDto);
                    var currentAntennas = storedSettingsDto.antennaPorts.Split(",");
                    capability.MaxAntennas = currentAntennas.Length;
                    capability.ValidAntennas = storedSettingsDto.antennaPorts;
                }
            }
        }
        catch (Exception)
        {
        }

        capabilities.Add(capability);
    }



    return Results.Ok(capabilities);
});

app.MapGet("/api/getrfidstatus", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    var rfidStatus = new List<object>();
    var statusEvent = new Dictionary<string, string>();
    try
    {
        var rshell = new RShellUtil(readerAddress, rshellAuthUserName, rshellAuthPassword);
        try
        {
            var resultRfidStat = rshell.SendCommand("show rfid stat");
            rshell.Disconnect();
            var lines = resultRfidStat.Split("\n");
            foreach (var line in lines)
            {
                if (line.StartsWith("status") || line.StartsWith("Status"))
                {
                    continue;
                }
                var values = line.Split("=");
                try
                {
                    statusEvent.Add(values[0], values[1].Replace("'", String.Empty));
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
    }
    catch (Exception)
    {

    }
    rfidStatus.Add(statusEvent);

    //var rfidStatus = new List<SmartReaderRfidStatus>();
    //var statusRf = new SmartReaderRfidStatus();
    //statusRf.Status = "0,Success";
    //statusRf.ReaderAdministrativeStatus = "enabled";
    //statusRf.ReaderOperationalStatus = "enabled";
    //statusRf.Antenna1AdministrativeStatus = "1";
    //statusRf.Antenna1OperationalStatus = "1";
    //statusRf.Antenna1LastPowerLevel = "0";
    //statusRf.Antenna2AdministrativeStatus = "1";
    //statusRf.Antenna2OperationalStatus = "1";
    //statusRf.Antenna2LastPowerLevel = "0";
    //statusRf.Antenna3AdministrativeStatus = "1";
    //statusRf.Antenna3OperationalStatus = "1";
    //statusRf.Antenna3LastPowerLevel = "0";
    //statusRf.Antenna4AdministrativeStatus = "1";
    //statusRf.Antenna4OperationalStatus = "1";
    //statusRf.Antenna4LastPowerLevel = "0";

    //rfidStatus.Add(statusRf);

    return Results.Ok(rfidStatus);
});

app.MapGet("/api/verify_key/{key}", [AuthorizeBasicAuth] async (RuntimeDb db, [FromRoute] string key) =>
{
    var readerLicenses = new List<ReaderLicense>();
    var readerLicense = new ReaderLicense();
    readerLicense.isValid = "fail";
    try
    {
        var serial = db.ReaderStatus.FindAsync("READER_SERIAL");

        if (serial != null)
        {

            var json = JsonConvert.DeserializeObject<List<SmartreaderSerialNumberDto>>(serial.Result.Value);
            var expectedLicense = Utils.CreateMD5Hash("sM@RTrEADER2022-" + json.FirstOrDefault().SerialNumber);
            if (string.Equals(key, expectedLicense, StringComparison.OrdinalIgnoreCase)) readerLicense.isValid = "pass";
            readerLicenses.Add(readerLicense);
            return Results.Ok(readerLicenses);
        }
    }
    catch (Exception)
    {
    }

    return Results.Ok(readerLicenses);
});

app.MapGet("/api/image", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    var imageStatus = new Dictionary<object, object>();
    logger.LogInformation("Requesting image status. ");
    var rshell = new RShellUtil(readerAddress, rshellAuthUserName, rshellAuthPassword);
    try
    {
        var resultImageStatus = rshell.SendCommand("show image summary");
        var lines = resultImageStatus.Split("\n");
        foreach (var line in lines)
        {
            logger.LogInformation(line);
            if(line.ToUpper().Contains("STATUS"))
            {
                continue;
            }
            var lineData = line.Split("=");
            if(lineData.Length > 1)
            {
                imageStatus.Add(lineData[0], lineData[1]);
            }
            
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "error loading image status");
    }

    return Results.Ok(imageStatus);
});

app.MapGet("/api/restore", [AuthorizeBasicAuth] async (RuntimeDb db) =>
{
    try
    {

        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/cp",
            Arguments = "/customer/config/smartreader_backup.json /customer/config/smartreader.json"
        };
        await Task.Delay(TimeSpan.FromSeconds(2));
        logger.LogInformation("Restarting process");
        // Restart the application by spawning a new process with the same arguments
        var process = Process.GetCurrentProcess();
        process.StartInfo.WorkingDirectory = Directory.GetCurrentDirectory();
        Process.Start(process.MainModule.FileName);
        Environment.Exit(1);

        return Results.Ok();
    }
    catch (Exception)
    {
        return Results.Ok();
    }
});

app.MapPost("/api/test", [AuthorizeBasicAuth] async ([FromBody] BearerDTO bearerDTO, RuntimeDb db) =>
{
    var token = "";
    try
    {
        if (!string.IsNullOrEmpty(bearerDTO.httpAuthenticationTokenApiUrl))
        {
            var url = bearerDTO.httpAuthenticationTokenApiUrl;
            var fullUriData = new Uri(bearerDTO.httpAuthenticationTokenApiUrl);
            var host = fullUriData.Host;
            var baseUri = fullUriData.GetLeftPart(UriPartial.Authority);
            var rightActionPath = url.Replace(baseUri, "");
            var configDto = ConfigFileHelper.ReadFile();
            var httpClientHandler = new HttpClientHandler
            {


                ServerCertificateCustomValidationCallback =
                   (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                       chain, errors) => true)
            };
            HttpClient httpClient = new()
            {
                BaseAddress = new Uri(bearerDTO.httpAuthenticationTokenApiUrl)
            };

            if (!string.IsNullOrEmpty(configDto.networkProxy))
            {
                WebProxy webProxy = new WebProxy(configDto.networkProxy, int.Parse(configDto.networkProxyPort));
                webProxy.Credentials = CredentialCache.DefaultNetworkCredentials;
                webProxy.BypassProxyOnLocal = true;
                webProxy.BypassList.Append("169.254.1.1");
                webProxy.BypassList.Append(readerAddress);
                webProxy.BypassList.Append("localhost");


                httpClientHandler = new HttpClientHandler
                {

                    Proxy = webProxy,
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback =
                    (Func<HttpRequestMessage, X509Certificate2, X509Chain, SslPolicyErrors, bool>)((message, cert,
                        chain, errors) => true)
                };
                httpClient = new HttpClient(httpClientHandler)
                {
                    BaseAddress = new Uri(bearerDTO.httpAuthenticationTokenApiUrl)
                };

            }



            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(bearerDTO.httpAuthenticationTokenApiUrl),
                Content = new StringContent(bearerDTO.httpAuthenticationTokenApiBody, Encoding.UTF8,
                    "application/json" /* or "application/json" in older versions */)
            };
            request.Headers.Add("Accept", "application/json");


            httpClient.DefaultRequestHeaders
                .Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            Log.Debug(url);
            Log.Debug(bearerDTO.httpAuthenticationTokenApiBody);
            Console.WriteLine(bearerDTO.httpAuthenticationTokenApiBody);

            var response = await httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            Log.Debug(content);
            Console.WriteLine(content);

            if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(response.Content.ReadAsStringAsync().Result))
            {
                var tokenResult = response.Content.ReadAsStringAsync().Result;
                try
                {
                    var tokenJson = JObject.Parse(tokenResult);
                    if (tokenJson.ContainsKey("token")) token = tokenJson.GetValue("token").ToString();
                }
                catch (Exception)
                {
                    return Results.BadRequest(token);
                }
            }
        }

        return Results.Ok(token);
    }
    catch (Exception exDb)
    {
        File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
        return Results.BadRequest(token);
    }
});

app.MapPost("/mode", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db) =>
{
    var requestResult = new Dictionary<object, object>();
    var payload = new Dictionary<object, object>();
    payload.Add("", "");

    try
    {
        var jsonDocumentStr = "";
        try
        {
            JsonElement commandIdNode = jsonDocument.RootElement.GetProperty("command_id");
            var commandId = commandIdNode.GetString();
            requestResult.Add("command_id", commandId);
            requestResult.Add("response", "queued");
            requestResult.Add("payload", payload);
        }
        catch (Exception ex)
        {
            requestResult.Add("response", "error");
            requestResult.Add("detail", ex.Message);
            requestResult.Add("payload", payload);
            return Results.BadRequest(requestResult);

        }


        using (var stream = new MemoryStream())
        {
            var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            jsonDocument.WriteTo(writer);
            writer.Flush();
            jsonDocumentStr = Encoding.UTF8.GetString(stream.ToArray());
            //request a start command
            var command = new ReaderCommands();
            command.Id = "MODE_COMMAND";
            command.Value = jsonDocumentStr;
            command.Timestamp = DateTime.Now;
            db.ReaderCommands.Add(command);
            await db.SaveChangesAsync();
        }

        return Results.Ok(requestResult);
    }
    catch (Exception exDb)
    {
        File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
        return Results.BadRequest(requestResult);
    }
});

app.MapPost("/mqtt", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db) =>
    {
        var requestResult = "";

        try
        {
            return ProcessMqttEndpointRequest(jsonDocument, db);
        }
        catch (Exception exDb)
        {
            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
            return Results.BadRequest(requestResult);
        }
    });

app.MapPut("/mqtt", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db) =>
    {
        var requestResult = "";

        try
        {
            return ProcessMqttEndpointRequest(jsonDocument, db);
        }
        catch (Exception exDb)
        {
            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
            return Results.BadRequest(requestResult);
        }
    });

app.MapGet("/mqtt", [AuthorizeBasicAuth] async (HttpRequest readerRequest, RuntimeDb db) =>
{
    var operationResult = new Dictionary<string, object>();
    var endPointList = new List<MqttConfigurationDto>();
    try
    {
        var mqttConfigurationDTO = new MqttConfigurationDto();
        mqttConfigurationDTO.Data = new Data();
        mqttConfigurationDTO.Data.Configuration = new Configuration();
        mqttConfigurationDTO.Data.Configuration.Additional = new Additional();
        mqttConfigurationDTO.Data.Configuration.Topics = new Topics();
        mqttConfigurationDTO.Data.Configuration.Topics.Control = new Control();
        mqttConfigurationDTO.Data.Configuration.Topics.Control.Command = new ManagementEvents();
        mqttConfigurationDTO.Data.Configuration.Topics.Control.Response = new ManagementEvents();
        mqttConfigurationDTO.Data.Configuration.Topics.Management = new Control();
        mqttConfigurationDTO.Data.Configuration.Topics.Management.Command = new ManagementEvents();
        mqttConfigurationDTO.Data.Configuration.Topics.Management.Response = new ManagementEvents();
        mqttConfigurationDTO.Data.Configuration.Topics.ManagementEvents = new ManagementEvents();
        mqttConfigurationDTO.Data.Configuration.Topics.TagEvents = new ManagementEvents();
        mqttConfigurationDTO.Data.Configuration.Endpoint = new Endpoint();


        var configDto = ConfigFileHelper.ReadFile();

        if (configDto != null && !string.IsNullOrEmpty(configDto.readerName))
        {
            mqttConfigurationDTO.Data.Configuration.Endpoint.Hostname = configDto.mqttBrokerAddress;
            mqttConfigurationDTO.Data.Configuration.Endpoint.Port = long.Parse(configDto.mqttBrokerPort);
            mqttConfigurationDTO.Data.Configuration.Endpoint.Protocol = configDto.mqttBrokerProtocol;

            mqttConfigurationDTO.Data.Configuration.Additional.CleanSession =
                Convert.ToBoolean(Convert.ToInt32(configDto.mqttBrokerCleanSession));
            mqttConfigurationDTO.Data.Configuration.Additional.ClientId = configDto.readerName;
            mqttConfigurationDTO.Data.Configuration.Additional.Debug =
                Convert.ToBoolean(Convert.ToInt32(configDto.mqttBrokerDebug));
            mqttConfigurationDTO.Data.Configuration.Additional.KeepAlive = long.Parse(configDto.mqttBrokerKeepAlive);

            mqttConfigurationDTO.Data.Configuration.Topics.Control.Command.Topic = configDto.mqttControlCommandTopic;
            mqttConfigurationDTO.Data.Configuration.Topics.Control.Command.Qos =
                long.Parse(configDto.mqttControlCommandQoS);
            mqttConfigurationDTO.Data.Configuration.Topics.Control.Command.Retain =
                bool.Parse(configDto.mqttControlCommandRetainMessages);

            mqttConfigurationDTO.Data.Configuration.Topics.Control.Response.Topic = configDto.mqttControlResponseTopic;
            mqttConfigurationDTO.Data.Configuration.Topics.Control.Response.Qos =
                long.Parse(configDto.mqttControlResponseQoS);
            mqttConfigurationDTO.Data.Configuration.Topics.Control.Response.Retain =
                bool.Parse(configDto.mqttControlResponseRetainMessages);

            mqttConfigurationDTO.Data.Configuration.Topics.Management.Command.Topic =
                configDto.mqttManagementCommandTopic;
            mqttConfigurationDTO.Data.Configuration.Topics.Management.Command.Qos =
                long.Parse(configDto.mqttManagementCommandQoS);
            mqttConfigurationDTO.Data.Configuration.Topics.Management.Command.Retain =
                bool.Parse(configDto.mqttManagementCommandRetainMessages);

            mqttConfigurationDTO.Data.Configuration.Topics.Management.Response.Topic =
                configDto.mqttManagementResponseTopic;
            mqttConfigurationDTO.Data.Configuration.Topics.Management.Response.Qos =
                long.Parse(configDto.mqttManagementResponseQoS);
            mqttConfigurationDTO.Data.Configuration.Topics.Management.Response.Retain =
                bool.Parse(configDto.mqttManagementResponseRetainMessages);

            mqttConfigurationDTO.Data.Configuration.Topics.ManagementEvents.Topic = configDto.mqttManagementEventsTopic;
            mqttConfigurationDTO.Data.Configuration.Topics.ManagementEvents.Qos =
                long.Parse(configDto.mqttManagementEventsQoS);
            mqttConfigurationDTO.Data.Configuration.Topics.ManagementEvents.Retain =
                bool.Parse(configDto.mqttManagementEventsRetainMessages);

            mqttConfigurationDTO.Data.Configuration.Topics.TagEvents.Topic = configDto.mqttTagEventsTopic;
            mqttConfigurationDTO.Data.Configuration.Topics.TagEvents.Qos = long.Parse(configDto.mqttTagEventsQoS);
            mqttConfigurationDTO.Data.Configuration.Topics.TagEvents.Retain =
                bool.Parse(configDto.mqttTagEventsRetainMessages);

            endPointList.Add(mqttConfigurationDTO);


            operationResult.Add("status", "OK");
            operationResult.Add("response", endPointList);
            return Results.Ok(operationResult);
        }

        operationResult.Add("status", "ERROR");
        return Results.NotFound(operationResult);
    }
    catch (Exception)
    {
        operationResult.Add("status", "ERROR");
        return Results.NotFound(operationResult);
    }
});

app.MapPost("/mqtt/command/control", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db, IotInterfaceService backgroundService) =>
{
    var requestResult = "";

    try
    {
        string json = "";
        using (var stream = new MemoryStream())
        {
            Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            jsonDocument.WriteTo(writer);
            writer.Flush();
            json = Encoding.UTF8.GetString(stream.ToArray());
        }
        //using var scope = context.RequestServices.CreateScope();
        //var backgroundService = scope.ServiceProvider.GetRequiredService<IIotInterfaceService>();
        requestResult = await backgroundService.ProcessMqttControlCommandJsonAsync(json, false);
        var result = JsonDocument.Parse(requestResult);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "error processing control command");
        File.WriteAllText(Path.Combine("/tmp", "error-mqtt-command.txt"), ex.Message);
        return Results.BadRequest(requestResult);
    }
});

app.MapPost("/mqtt/command/management", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db, IotInterfaceService backgroundService) =>
{
    var requestResult = "";

    try
    {
        string json = "";
        using (var stream = new MemoryStream())
        {
            Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            jsonDocument.WriteTo(writer);
            writer.Flush();
            json = Encoding.UTF8.GetString(stream.ToArray());
        }
        //using var scope = context.RequestServices.CreateScope();
        //var backgroundService = scope.ServiceProvider.GetRequiredService<IIotInterfaceService>();

        requestResult = await backgroundService.ProcessMqttManagementCommandJsonAsync(json, false);
        var result = JsonDocument.Parse(requestResult);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "error processing control command");
        File.WriteAllText(Path.Combine("/tmp", "error-mqtt-command.txt"), ex.Message);
        return Results.BadRequest(requestResult);
    }
});

app.MapPost("/mqtt/command/mode", [AuthorizeBasicAuth]
async (HttpRequest readerRequest, [FromBody] JsonDocument jsonDocument, RuntimeDb db, IotInterfaceService backgroundService) =>
{
    var commandStatus = "success";

    try
    {
        string json = "";
        using (var stream = new MemoryStream())
        {
            Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            jsonDocument.WriteTo(writer);
            writer.Flush();
            json = Encoding.UTF8.GetString(stream.ToArray());
        }
        //using var scope = context.RequestServices.CreateScope();
        //var backgroundService = scope.ServiceProvider.GetRequiredService<IIotInterfaceService>();
        var modeCmdResult = "success";
        modeCmdResult = backgroundService.ProcessMqttModeJsonCommand(json);

        if (!"success".Equals(modeCmdResult)) commandStatus = "error";

        var deserializedCmdData = JsonConvert.DeserializeObject<JObject>(json);
        if (deserializedCmdData.ContainsKey("response"))
        {
            deserializedCmdData["response"] = commandStatus;
        }
        else
        {
            var commandResponse = new JProperty("response", commandStatus);
            deserializedCmdData.Add(commandResponse);
        }

        if (deserializedCmdData.ContainsKey("message"))
        {
            deserializedCmdData["message"] = modeCmdResult;
        }
        else
        {
            var commandResponse = new JProperty("message", modeCmdResult);
            deserializedCmdData.Add(commandResponse);
        }

        var serializedData = JsonConvert.SerializeObject(deserializedCmdData);

        var result = JsonDocument.Parse(serializedData);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "error processing control command");
        File.WriteAllText(Path.Combine("/tmp", "error-mqtt-command.txt"), ex.Message);
        return Results.BadRequest(commandStatus);
    }
});

app.MapPost("/upload/mqtt/ca", [AuthorizeBasicAuth]
async (HttpRequest request, RuntimeDb db) =>
{
    var requestResult = "Error saving file.";

    try
    {
        if (!request.Form.Files.Any())
        {
            return Results.BadRequest("Atleast one file is needed");
        }

        if (!Directory.Exists(@"/customer/config/ca/"))
        {
            try
            {
                Directory.CreateDirectory(@"/customer/config/ca/");
            }
            catch (Exception ex)
            {

                logger.LogError(ex, "Error creating CA directory.");
            }
        }

        foreach (var file in request.Form.Files)
        {
            using (var stream = new FileStream(@"/customer/config/ca/" + file.FileName, FileMode.Create))
            {
                file.CopyTo(stream);
            }
            break;
        }

        return Results.Ok("File Uploaded Sucessuful");
    }
    catch (Exception exDb)
    {
        File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
        return Results.BadRequest(requestResult);
    }
});

app.MapPost("/upload/mqtt/certificate", [AuthorizeBasicAuth]
async (HttpRequest request, RuntimeDb db) =>
{
    var requestResult = "Error saving file.";

    try
    {
        if (!request.Form.Files.Any())
        {
            return Results.BadRequest("At least one file is needed");
        }

        if (!Directory.Exists(@"/customer/config/certificate/"))
        {
            try
            {
                Directory.CreateDirectory(@"/customer/config/certificate/");
            }
            catch (Exception ex)
            {

                logger.LogError(ex, "Error creating CA directory.");
            }
        }

        foreach (var file in request.Form.Files)
        {
            using (var stream = new FileStream(@"/customer/config/certificate/" + file.FileName, FileMode.Create))
            {
                file.CopyTo(stream);
            }
            break;
        }

        return Results.Ok("File Uploaded Sucessuful");
    }
    catch (Exception exDb)
    {
        File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb.Message);
        return Results.BadRequest(requestResult);
    }
});

app.UseSwagger();
app.UseSwaggerUI();

// Subscribe to SIGTERM
var shutdownSignal = new ManualResetEventSlim(false);

AssemblyLoadContext.Default.Unloading += ctx =>
{
    Console.WriteLine("Received SIGTERM signal. Stopping gracefully...");

    // Trigger graceful shutdown
    shutdownSignal.Set();
};

// Run the app
var hostTask = Task.Run(() =>
{
    app.Run();
});

// Wait for SIGTERM or application completion
if (shutdownSignal.Wait(TimeSpan.FromSeconds(2)))
{
    Console.WriteLine("Graceful shutdown completed.");
}
else
{
    Console.WriteLine("Shutdown timeout. Exiting forcefully.");
}

await hostTask;

// Cleanup resources if needed

// Dispose the app and services
await app.DisposeAsync();

// app.Run();

static IResult ProcessMqttEndpointRequest(JsonDocument jsonDocument, RuntimeDb db)
{
    if (jsonDocument != null)
    {
        var jsonDocumentStr = "";
        MqttConfigurationDto mqttConfigurationDto = null;
        using (var stream = new MemoryStream())
        {
            var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            jsonDocument.WriteTo(writer);
            writer.Flush();
            jsonDocumentStr = Encoding.UTF8.GetString(stream.ToArray());
            mqttConfigurationDto = MqttConfigurationDto.FromJson(jsonDocumentStr);
        }

        if (mqttConfigurationDto != null)
        {
            var configDto = ConfigFileHelper.ReadFile();
            configDto = StandaloneConfigDTO.CleanupUrlEncoding(configDto);

            if (configDto != null)
            {
                if (!string.IsNullOrEmpty(mqttConfigurationDto.SessionId))
                {
                    var connectionResult = "";
                    if (string.Equals("1", configDto.mqttEnabled,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        configDto.mqttEnabled = "0";
                        connectionResult = "Successfully connected with the endpoint";
                    }
                    else
                    {
                        configDto.mqttEnabled = "1";
                        connectionResult = "Successfully disconnected from the endpoint";
                    }

                    var currentConfigDto = ConfigFileHelper.ReadFile();
                    if (currentConfigDto != null && !currentConfigDto.Equals(configDto))
                        try
                        {
                            var configModel = db.ReaderConfigs.FindAsync("READER_CONFIG").Result;
                            if (configModel == null)
                            {
                                configModel = new ReaderConfigs();
                                configModel.Id = "READER_CONFIG";
                                configModel.Value = JsonConvert.SerializeObject(configDto);
                                db.ReaderConfigs.Add(configModel);
                            }
                            else
                            {
                                configModel.Value = JsonConvert.SerializeObject(configDto);
                                db.ReaderConfigs.Update(configModel);
                            }

                            db.SaveChangesAsync();
                            var operationResult = new Dictionary<string, string>();
                            operationResult.Add("status", "OK");
                            operationResult.Add("message", connectionResult);
                            return Results.Ok(operationResult);
                        }
                        catch (Exception exDb1)
                        {
                            File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb1.Message);
                        }
                }

                if (!string.IsNullOrEmpty(mqttConfigurationDto.Operation)
                    && (string.Equals("ADD", mqttConfigurationDto.Operation,
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals("UPDATE", mqttConfigurationDto.Operation,
                            StringComparison.OrdinalIgnoreCase)))
                    if (mqttConfigurationDto.Data != null)
                    {
                        if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Name)
                            && !string.Equals(configDto.mqttBrokerName, mqttConfigurationDto.Data.Name,
                                StringComparison.OrdinalIgnoreCase))
                            configDto.mqttBrokerName = mqttConfigurationDto.Data.Name;

                        if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Description)
                            && !string.Equals(configDto.mqttBrokerDescription, mqttConfigurationDto.Data.Description,
                                StringComparison.OrdinalIgnoreCase))
                            configDto.mqttBrokerDescription = mqttConfigurationDto.Data.Description;

                        if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Type)
                            && !string.Equals(configDto.mqttBrokerDescription, mqttConfigurationDto.Data.Type,
                                StringComparison.OrdinalIgnoreCase))
                            configDto.mqttBrokerDescription = mqttConfigurationDto.Data.Type;

                        if (mqttConfigurationDto.Data.Configuration != null)
                        {
                            if (mqttConfigurationDto.Data.Configuration.Endpoint != null)
                            {
                                if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Endpoint.Hostname)
                                    && !string.Equals(configDto.mqttBrokerAddress,
                                        mqttConfigurationDto.Data.Configuration.Endpoint.Hostname,
                                        StringComparison.OrdinalIgnoreCase))
                                    configDto.mqttBrokerAddress =
                                        mqttConfigurationDto.Data.Configuration.Endpoint.Hostname;

                                if (mqttConfigurationDto.Data.Configuration.Endpoint.Port.HasValue
                                    && !string.Equals(configDto.mqttBrokerPort,
                                        mqttConfigurationDto.Data.Configuration.Endpoint.Port.ToString(),
                                        StringComparison.OrdinalIgnoreCase))
                                    configDto.mqttBrokerAddress =
                                        mqttConfigurationDto.Data.Configuration.Endpoint.Port.ToString();

                                if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Endpoint.Protocol)
                                    && !string.Equals(configDto.mqttBrokerProtocol,
                                        mqttConfigurationDto.Data.Configuration.Endpoint.Protocol,
                                        StringComparison.OrdinalIgnoreCase))
                                    configDto.mqttBrokerProtocol =
                                        mqttConfigurationDto.Data.Configuration.Endpoint.Protocol;
                            }

                            if (mqttConfigurationDto.Data.Configuration.Additional != null)
                            {
                                if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Additional.ClientId)
                                    && !string.Equals(configDto.readerName,
                                        mqttConfigurationDto.Data.Configuration.Additional.ClientId,
                                        StringComparison.OrdinalIgnoreCase))
                                    configDto.readerName = mqttConfigurationDto.Data.Configuration.Additional.ClientId;

                                if (mqttConfigurationDto.Data.Configuration.Additional.CleanSession.HasValue
                                    && !string.Equals(configDto.mqttBrokerCleanSession,
                                        mqttConfigurationDto.Data.Configuration.Additional.CleanSession.ToString(),
                                        StringComparison.OrdinalIgnoreCase))
                                {
                                    if (mqttConfigurationDto.Data.Configuration.Additional.CleanSession.Value)
                                        configDto.mqttBrokerCleanSession = "1";
                                    else
                                        configDto.mqttBrokerCleanSession = "0";
                                }

                                if (mqttConfigurationDto.Data.Configuration.Additional.KeepAlive.HasValue
                                    && !string.Equals(configDto.mqttBrokerKeepAlive,
                                        mqttConfigurationDto.Data.Configuration.Additional.KeepAlive.ToString(),
                                        StringComparison.OrdinalIgnoreCase))
                                    configDto.mqttBrokerKeepAlive = mqttConfigurationDto.Data.Configuration.Additional
                                        .KeepAlive.ToString();

                                if (mqttConfigurationDto.Data.Configuration.Additional.Debug.HasValue
                                    && !string.Equals(configDto.mqttBrokerDebug,
                                        mqttConfigurationDto.Data.Configuration.Additional.Debug.ToString(),
                                        StringComparison.OrdinalIgnoreCase))
                                    configDto.mqttBrokerDebug =
                                        mqttConfigurationDto.Data.Configuration.Additional.Debug.ToString();
                            }

                            if (mqttConfigurationDto.Data.Configuration.Topics != null)
                            {
                                if (mqttConfigurationDto.Data.Configuration.Topics.TagEvents != null)
                                {
                                    if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Topics.TagEvents
                                            .Topic)
                                        && !string.Equals(configDto.mqttTagEventsTopic,
                                            mqttConfigurationDto.Data.Configuration.Topics.TagEvents.Topic,
                                            StringComparison.OrdinalIgnoreCase))
                                        configDto.mqttTagEventsTopic = mqttConfigurationDto.Data.Configuration.Topics
                                            .TagEvents.Topic;

                                    if (mqttConfigurationDto.Data.Configuration.Topics.TagEvents.Qos.HasValue
                                        && !string.Equals(configDto.mqttTagEventsQoS,
                                            mqttConfigurationDto.Data.Configuration.Topics.TagEvents.Qos.ToString(),
                                            StringComparison.OrdinalIgnoreCase))
                                        configDto.mqttTagEventsQoS = mqttConfigurationDto.Data.Configuration.Topics
                                            .TagEvents.Qos.ToString();

                                    if (mqttConfigurationDto.Data.Configuration.Topics.TagEvents.Retain.HasValue
                                        && !string.Equals(configDto.mqttTagEventsRetainMessages,
                                            mqttConfigurationDto.Data.Configuration.Topics.TagEvents.Retain.ToString(),
                                            StringComparison.OrdinalIgnoreCase))
                                        configDto.mqttTagEventsRetainMessages = mqttConfigurationDto.Data.Configuration
                                            .Topics.TagEvents.Retain.ToString();
                                }

                                if (mqttConfigurationDto.Data.Configuration.Topics.ManagementEvents != null)
                                {
                                    if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Topics
                                            .ManagementEvents.Topic)
                                        && !string.Equals(configDto.mqttManagementEventsTopic,
                                            mqttConfigurationDto.Data.Configuration.Topics.TagEvents.Topic,
                                            StringComparison.OrdinalIgnoreCase))
                                        configDto.mqttManagementEventsTopic = mqttConfigurationDto.Data.Configuration
                                            .Topics.ManagementEvents.Topic;

                                    if (mqttConfigurationDto.Data.Configuration.Topics.ManagementEvents.Qos.HasValue
                                        && !string.Equals(configDto.mqttManagementEventsQoS,
                                            mqttConfigurationDto.Data.Configuration.Topics.ManagementEvents.Qos
                                                .ToString(),
                                            StringComparison.OrdinalIgnoreCase))
                                        configDto.mqttManagementEventsQoS = mqttConfigurationDto.Data.Configuration
                                            .Topics.ManagementEvents.Qos.ToString();

                                    if (mqttConfigurationDto.Data.Configuration.Topics.ManagementEvents.Retain.HasValue
                                        && !string.Equals(configDto.mqttManagementEventsRetainMessages,
                                            mqttConfigurationDto.Data.Configuration.Topics.ManagementEvents.Retain
                                                .ToString(),
                                            StringComparison.OrdinalIgnoreCase))
                                        configDto.mqttManagementEventsRetainMessages = mqttConfigurationDto.Data
                                            .Configuration.Topics.ManagementEvents.Retain.ToString();
                                }

                                if (mqttConfigurationDto.Data.Configuration.Topics.Management != null)
                                {
                                    if (mqttConfigurationDto.Data.Configuration.Topics.Management.Command != null)
                                    {
                                        if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Topics
                                                .Management.Command.Topic)
                                            && !string.Equals(configDto.mqttManagementCommandTopic,
                                                mqttConfigurationDto.Data.Configuration.Topics.Management.Command.Topic,
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttManagementCommandTopic = mqttConfigurationDto.Data
                                                .Configuration.Topics.Management.Command.Topic;

                                        if (mqttConfigurationDto.Data.Configuration.Topics.Management.Command.Qos
                                                .HasValue
                                            && !string.Equals(configDto.mqttManagementCommandQoS,
                                                mqttConfigurationDto.Data.Configuration.Topics.Management.Command
                                                    .ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttManagementCommandQoS = mqttConfigurationDto.Data.Configuration
                                                .Topics.Management.Command.Qos.ToString();

                                        if (mqttConfigurationDto.Data.Configuration.Topics.Management.Command.Retain
                                                .HasValue
                                            && !string.Equals(configDto.mqttManagementCommandRetainMessages,
                                                mqttConfigurationDto.Data.Configuration.Topics.Management.Command.Retain
                                                    .ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttManagementCommandRetainMessages = mqttConfigurationDto.Data
                                                .Configuration.Topics.Management.Command.Retain.ToString();
                                    }

                                    if (mqttConfigurationDto.Data.Configuration.Topics.Management.Response != null)
                                    {
                                        if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Topics
                                                .Management.Response.Topic)
                                            && !string.Equals(configDto.mqttManagementResponseTopic,
                                                mqttConfigurationDto.Data.Configuration.Topics.Management.Response
                                                    .Topic,
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttManagementResponseTopic = mqttConfigurationDto.Data
                                                .Configuration.Topics.Management.Response.Topic;

                                        if (mqttConfigurationDto.Data.Configuration.Topics.Management.Response.Qos
                                                .HasValue
                                            && !string.Equals(configDto.mqttManagementResponseQoS,
                                                mqttConfigurationDto.Data.Configuration.Topics.Management.Response
                                                    .ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttManagementResponseQoS = mqttConfigurationDto.Data
                                                .Configuration.Topics.Management.Response.Qos.ToString();

                                        if (mqttConfigurationDto.Data.Configuration.Topics.Management.Response.Retain
                                                .HasValue
                                            && !string.Equals(configDto.mqttManagementResponseRetainMessages,
                                                mqttConfigurationDto.Data.Configuration.Topics.Management.Response
                                                    .Retain.ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttManagementResponseRetainMessages = mqttConfigurationDto.Data
                                                .Configuration.Topics.Management.Response.Retain.ToString();
                                    }
                                }

                                if (mqttConfigurationDto.Data.Configuration.Topics.Control != null)
                                {
                                    if (mqttConfigurationDto.Data.Configuration.Topics.Control.Command != null)
                                    {
                                        if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Topics.Control
                                                .Command.Topic)
                                            && !string.Equals(configDto.mqttControlCommandTopic,
                                                mqttConfigurationDto.Data.Configuration.Topics.Control.Command.Topic,
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttControlCommandTopic = mqttConfigurationDto.Data.Configuration
                                                .Topics.Control.Command.Topic;

                                        if (mqttConfigurationDto.Data.Configuration.Topics.Control.Command.Qos.HasValue
                                            && !string.Equals(configDto.mqttControlCommandQoS,
                                                mqttConfigurationDto.Data.Configuration.Topics.Control.Command
                                                    .ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttControlCommandQoS = mqttConfigurationDto.Data.Configuration
                                                .Topics.Control.Command.Qos.ToString();

                                        if (mqttConfigurationDto.Data.Configuration.Topics.Control.Command.Retain
                                                .HasValue
                                            && !string.Equals(configDto.mqttControlCommandRetainMessages,
                                                mqttConfigurationDto.Data.Configuration.Topics.Control.Command.Retain
                                                    .ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttControlCommandRetainMessages = mqttConfigurationDto.Data
                                                .Configuration.Topics.Control.Command.Retain.ToString();
                                    }

                                    if (mqttConfigurationDto.Data.Configuration.Topics.Control.Response != null)
                                    {
                                        if (!string.IsNullOrEmpty(mqttConfigurationDto.Data.Configuration.Topics.Control
                                                .Response.Topic)
                                            && !string.Equals(configDto.mqttControlResponseTopic,
                                                mqttConfigurationDto.Data.Configuration.Topics.Control.Response.Topic,
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttControlResponseTopic = mqttConfigurationDto.Data.Configuration
                                                .Topics.Control.Response.Topic;

                                        if (mqttConfigurationDto.Data.Configuration.Topics.Control.Response.Qos.HasValue
                                            && !string.Equals(configDto.mqttControlResponseQoS,
                                                mqttConfigurationDto.Data.Configuration.Topics.Control.Response
                                                    .ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttControlResponseQoS = mqttConfigurationDto.Data.Configuration
                                                .Topics.Control.Response.Qos.ToString();

                                        if (mqttConfigurationDto.Data.Configuration.Topics.Control.Response.Retain
                                                .HasValue
                                            && !string.Equals(configDto.mqttControlResponseRetainMessages,
                                                mqttConfigurationDto.Data.Configuration.Topics.Control.Response.Retain
                                                    .ToString(),
                                                StringComparison.OrdinalIgnoreCase))
                                            configDto.mqttControlResponseRetainMessages = mqttConfigurationDto.Data
                                                .Configuration.Topics.Control.Response.Retain.ToString();
                                    }
                                }
                            }
                        }

                        var currentConfigDto = ConfigFileHelper.ReadFile();
                        if (currentConfigDto != null && !currentConfigDto.Equals(configDto))
                            try
                            {
                                var configModel = db.ReaderConfigs.FindAsync("READER_CONFIG").Result;
                                if (configModel == null)
                                {
                                    configModel = new ReaderConfigs();
                                    configModel.Id = "READER_CONFIG";
                                    configModel.Value = JsonConvert.SerializeObject(configDto);
                                    db.ReaderConfigs.Add(configModel);
                                }
                                else
                                {
                                    configModel.Value = JsonConvert.SerializeObject(configDto);
                                    db.ReaderConfigs.Update(configModel);
                                }

                                db.SaveChangesAsync();
                                var operationResult = new Dictionary<string, string>();
                                operationResult.Add("status", "OK");
                                operationResult.Add("message", "Successfully added endpoint configurations");
                                return Results.Ok(operationResult);
                            }
                            catch (Exception exDb1)
                            {
                                File.WriteAllText(Path.Combine("/tmp", "error-db.txt"), exDb1.Message);
                            }
                    }
            }
        }
    }

    var operationResulterror = new Dictionary<string, string>();
    operationResulterror.Add("status", "NOT PROCESSED");
    operationResulterror.Add("message", "The request was not processed");

    return Results.BadRequest(operationResulterror);
}


