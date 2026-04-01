using AMR.Communication;
using AMR.Service;
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

        builder.RegisterType<CameraService>()
            .AsSelf()
            .SingleInstance();
    }
}
