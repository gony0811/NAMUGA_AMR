using System.Text;
using AMR;
using AMR.Communication;
using AMR.Data;
using AMR.Service;
using AMR.Web.Services;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Serilog;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Serilog 설정
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Autofac을 DI 컨테이너로 사용
builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory());
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    // AMR 라이브러리 모듈 등록
    containerBuilder.RegisterModule<AmrModule>();

    // Web 서비스 등록
    containerBuilder.RegisterType<SettingsService>().AsSelf().SingleInstance();
    containerBuilder.RegisterType<SimulationService>().AsSelf().SingleInstance();

    // 카메라 설정을 appsettings.json에서 로드하여 등록
    containerBuilder.Register(c =>
    {
        var section = builder.Configuration.GetSection("CameraSettings");
        return new CameraSettings
        {
            DeviceIndex = section.GetValue("DeviceIndex", 0),
            FrameWidth = section.GetValue("FrameWidth", 1280),
            FrameHeight = section.GetValue("FrameHeight", 720),
            DepthFrameWidth = section.GetValue("DepthFrameWidth", 640),
            DepthFrameHeight = section.GetValue("DepthFrameHeight", 480),
            TargetFps = section.GetValue("TargetFps", 15),
            JpegQuality = section.GetValue("JpegQuality", 75),
            DepthFx = section.GetValue("DepthFx", 570.0),
            DepthFy = section.GetValue("DepthFy", 570.0)
        };
    }).As<CameraSettings>().SingleInstance();

    // Modbus 설정을 appsettings.json에서 로드하여 등록
    containerBuilder.Register(c =>
    {
        var modbus = c.Resolve<SettingsService>().LoadModbus();
        return new AmrModbusTcpSettings
        {
            IpAddress = modbus.IpAddress,
            Port = modbus.Port,
            SlaveId = modbus.SlaveId
        };
    }).As<AmrModbusTcpSettings>().SingleInstance();

    // Cobot Modbus 설정을 appsettings.json에서 로드하여 등록
    containerBuilder.Register(c =>
    {
        var cobot = c.Resolve<SettingsService>().LoadCobotModbus();
        return new CobotModbusTcpSettings
        {
            IpAddress = cobot.IpAddress,
            Port = cobot.Port,
            SlaveId = cobot.SlaveId
        };
    }).As<CobotModbusTcpSettings>().SingleInstance();

    // I/O Module (LS XEL-BSSRT) Modbus 설정
    containerBuilder.Register(c =>
    {
        var io = c.Resolve<SettingsService>().LoadIoModuleModbus();
        return new IoModuleModbusTcpSettings
        {
            IpAddress = io.IpAddress,
            Port = io.Port,
            SlaveId = io.SlaveId
        };
    }).As<IoModuleModbusTcpSettings>().SingleInstance();

    // MQTT 설정을 appsettings.json에서 로드하여 등록
    containerBuilder.Register(c =>
    {
        var mqtt = c.Resolve<SettingsService>().LoadMqtt();
        var section = builder.Configuration.GetSection("MqttSettings");
        return new MqttClientSettings
        {
            BrokerAddress = mqtt.BrokerAddress,
            BrokerPort = mqtt.BrokerPort,
            ClientId = mqtt.ClientId,
            Username = mqtt.Username,
            Password = mqtt.Password,
            PublishIntervalMs = section.GetValue("PublishIntervalMs", 1000)
        };
    }).As<MqttClientSettings>().SingleInstance();
});

// BackgroundService 등록
builder.Services.AddHostedService(sp => sp.GetRequiredService<AmrService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CobotService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<IoModuleService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MainSequenceService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CameraService>());

// SQLite 데이터베이스 설정
var connectionString = "Data Source=amr.db";
builder.Services.AddDbContext<AmrDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDbContextFactory<AmrDbContext>(options =>
    options.UseSqlite(connectionString));

// Add services to the container.
builder.Services.AddRazorPages();

var app = builder.Build();

// 데이터베이스 마이그레이션 자동 적용
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AmrDbContext>();
    db.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
