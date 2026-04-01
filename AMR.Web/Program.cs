using AMR;
using AMR.Communication;
using AMR.Service;
using AMR.Web.Services;
using Autofac;
using Autofac.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

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
            TargetFps = section.GetValue("TargetFps", 20),
            JpegQuality = section.GetValue("JpegQuality", 75)
        };
    }).As<CameraSettings>().SingleInstance();

    // Depth 카메라 설정을 appsettings.json에서 로드하여 등록
    containerBuilder.Register(c =>
    {
        var section = builder.Configuration.GetSection("DepthCameraSettings");
        return new DepthCameraSettings
        {
            DeviceIndex = section.GetValue("DeviceIndex", 0),
            FrameWidth = section.GetValue("FrameWidth", 640),
            FrameHeight = section.GetValue("FrameHeight", 480),
            TargetFps = section.GetValue("TargetFps", 15),
            JpegQuality = section.GetValue("JpegQuality", 75)
        };
    }).As<DepthCameraSettings>().SingleInstance();

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
builder.Services.AddHostedService(sp => sp.GetRequiredService<MainSequenceService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<CameraService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<DepthCameraService>());

// Add services to the container.
builder.Services.AddRazorPages();

var app = builder.Build();

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
