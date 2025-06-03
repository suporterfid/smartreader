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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using MQTTnet;
using MQTTnet.Server;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SmartReader.Infrastructure.Database;
using SmartReader.IotDeviceInterface;
using SmartReaderStandalone.Authentication;
using SmartReaderStandalone.IotDeviceInterface;
using SmartReaderStandalone.Services;
using SmartReaderStandalone.Utils;
using System.Net;
using System.Runtime.Loader;
using static SmartReader.IotDeviceInterface.R700IotReader;

if (File.Exists("/customer/upgrading"))
{
    Environment.Exit(0);
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var levelSwitch = new LoggingLevelSwitch
{
    MinimumLevel = LogEventLevel.Information // Default level
};

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("/customer/appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("/customer/customsettings.json", optional: false, reloadOnChange: true);

// Register IConfiguration in services
builder.Services.AddSingleton(builder.Configuration);

LogEventLevel logEventLevel = LogEventLevel.Warning;
#if DEBUG
logEventLevel = LogEventLevel.Debug;
#endif
builder.Host.UseSerilog((ctx, lc) => lc
    .WriteTo.Console()
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.ControlledBy(levelSwitch)
    .WriteTo.File("/customer/wwwroot/logs/log.txt", logEventLevel,
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        fileSizeLimitBytes: 102400, rollOnFileSizeLimit: true, retainedFileCountLimit: 2));

builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.AddSerilog();
});

builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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

// Register MetricsMonitoringService ONLY as IHostedService (NOT as IMetricProvider)
builder.Services.AddSingleton<MetricsMonitoringService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<MetricsMonitoringService>());
builder.Services.AddSingleton(levelSwitch);
builder.Services.AddSingleton<LoggingService>();
builder.Services.AddSingleton<ISmartReaderConfigurationService, SmartReaderConfigurationService>();

builder.Services.AddSingleton<IReaderConfiguration>(sp =>
{
    var logger = sp.GetService<ILogger<ReaderConfiguration>>();
    var readerConfig = ReaderConfiguration.CreateDefault();

    try
    {
        var configPath = "/customer/appsettings.json";
        if (File.Exists(configPath))
        {
            readerConfig = SmartReaderStandalone.IotDeviceInterface.ConfigurationExtensions.LoadFromJson(configPath);
        }
        readerConfig.SetLogger(logger);
        return readerConfig;
    }
    catch (Exception ex)
    {
        logger?.LogError(ex, "Failed to load reader configuration from file. Using defaults.");
        var defaultConfig = ReaderConfiguration.CreateDefault();
        defaultConfig.SetLogger(logger);
        return defaultConfig;
    }
});
builder.Services.AddSingleton<ReaderConfiguration>(sp =>
{
    var logger = sp.GetService<ILogger<ReaderConfiguration>>();
    var readerConfig = ReaderConfiguration.CreateDefault();

    try
    {
        var configPath = "/customer/appsettings.json";
        if (File.Exists(configPath))
        {
            readerConfig = SmartReaderStandalone.IotDeviceInterface.ConfigurationExtensions.LoadFromJson(configPath);
        }
        readerConfig.SetLogger(logger);
        return readerConfig;
    }
    catch (Exception ex)
    {
        logger?.LogError(ex, "Failed to load reader configuration from file. Using defaults.");
        var defaultConfig = ReaderConfiguration.CreateDefault();
        defaultConfig.SetLogger(logger);
        return defaultConfig;
    }
});

builder.Services.AddSingleton<IR700IotReader>(provider =>
{
    var configuration = provider.GetRequiredService<ReaderConfiguration>();
    var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
    var logger = provider.GetRequiredService<ILogger<R700IotReader>>();
    var configService = provider.GetRequiredService<ISmartReaderConfigurationService>();
    var hostname = configService.GetReaderAddress();

    return new R700IotReader(
        hostname,
        configuration,
        logger,
        loggerFactory
    );
});

builder.Services.AddSingleton<ITcpSocketService, TcpSocketService>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILogger<TcpSocketService>>();
    var configurationService = serviceProvider.GetRequiredService<ISmartReaderConfigurationService>();

    return new TcpSocketService(serviceProvider, configuration, logger, configurationService);
});

builder.Services.AddSingleton<IWebSocketService, WebSocketService>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILogger<WebSocketService>>();
    var configurationService = serviceProvider.GetRequiredService<ISmartReaderConfigurationService>();

    return new WebSocketService(serviceProvider, configuration, logger, configurationService);
});

builder.Services.AddSingleton<IMqttService, MqttService>(serviceProvider =>
{
    var reader = serviceProvider.GetRequiredService<IR700IotReader>();
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILogger<MqttService>>();
    var configurationService = serviceProvider.GetRequiredService<ISmartReaderConfigurationService>();
    var standoaloneConfigDTO = configurationService.LoadConfig();

    return new MqttService(logger, standoaloneConfigDTO);
});

// Register GPO service
builder.Services.AddSingleton<IGpoService>(serviceProvider =>
{
    var reader = serviceProvider.GetRequiredService<IR700IotReader>();
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILogger<GpoService>>();
    var readerConfiguration = serviceProvider.GetRequiredService<ReaderConfiguration>();

    return new GpoService(reader, logger, readerConfiguration);
});

builder.Services.AddSingleton<IHealthMonitor>(provider =>
{
    var logger = provider.GetRequiredService<ILogger<HealthMonitor>>();
    return new HealthMonitor(logger);
});

builder.Services.AddSingleton<IotInterfaceService>();
builder.Services.AddSingleton<IHostedService, IotInterfaceService>(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var logger = serviceProvider.GetRequiredService<ILogger<IotInterfaceService>>();
    var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var configurationService = serviceProvider.GetRequiredService<ISmartReaderConfigurationService>();
    var tcpSocketService = serviceProvider.GetRequiredService<ITcpSocketService>();
    var mqttService = serviceProvider.GetRequiredService<IMqttService>();
    var webSocketService = serviceProvider.GetRequiredService<IWebSocketService>();
    var gpoService = serviceProvider.GetRequiredService<IGpoService>();

    return new IotInterfaceService(serviceProvider,
        configuration,
        logger,
        loggerFactory,
        httpClientFactory,
        configurationService,
        tcpSocketService,
        mqttService,
        webSocketService,
        gpoService);
});
builder.Services.AddScoped<ISummaryQueueBackgroundService, SummaryQueueBackgroundService>();

// Create and start the MQTT servers
var mqttFactory = new MqttFactory();
MqttServer? tcpMqttServer = null;
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
    // Ignore MQTT server startup error for now
}

var app = builder.Build();

// Startup info, logging, env detection...
var eventProcessorLogger = app.Services.GetRequiredService<ILogger<Program>>();
var readerConfiguration = app.Services.GetRequiredService<ReaderConfiguration>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// custom basic auth middleware
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<BasicAuthMiddleware>();
app.UseHttpsRedirection();

app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = ["index.html"]
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
            ctx.Context.Response.Headers.Append("WWW-Authenticate", string.Format("Basic realm=\"{0}\"", "R700"));
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

app.UseRouting();
app.MapControllers();

app.UseCors("AllowAll");

// Subscribe to SIGTERM
var shutdownSignal = new ManualResetEventSlim(false);
AssemblyLoadContext.Default.Unloading += ctx =>
{
    Console.WriteLine("Received SIGTERM signal. Stopping gracefully...");
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
await app.DisposeAsync();
