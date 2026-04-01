using System.Runtime.InteropServices;
using AMR.Communication;
using AMR.Service;
using AMR.Service.Camera;
using Autofac;

namespace AMR;

/// <summary>
/// AMR 라이브러리 서비스 등록 모듈
/// </summary>
public class AmrModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Communication
        builder.RegisterType<AmrModbusTcpClient>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<CobotModbusTcpClient>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AmrMqttClient>()
            .AsSelf()
            .SingleInstance();

        // Service
        builder.RegisterType<AmrService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<CobotService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MqttService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainSequenceService>()
            .AsSelf()
            .SingleInstance();

        // Camera - 플랫폼별 프로바이더 등록
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            builder.RegisterType<OrbbecSdkProvider>()
                .As<ICameraProvider>()
                .SingleInstance();
        }
        else
        {
            builder.RegisterType<OpenCvObsensorProvider>()
                .As<ICameraProvider>()
                .SingleInstance();
        }

        builder.RegisterType<CameraService>()
            .AsSelf()
            .SingleInstance();
    }
}
