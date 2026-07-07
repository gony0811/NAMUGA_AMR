using System.Text.Json;
using System.Text.Json.Nodes;
using AMR.Web.Models;

namespace AMR.Web.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private const string MqttSectionName = "MqttSettings";
    private const string ModbusSectionName = "ModbusSettings";
    private const string CobotModbusSectionName = "CobotModbusSettings";
    private const string IoModuleModbusSectionName = "IoModuleModbusSettings";
    private const string AutoChargeSectionName = "AutoChargeSettings";

    public SettingsService(IWebHostEnvironment env)
    {
        _settingsFilePath = Path.Combine(env.ContentRootPath, "appsettings.json");
    }

    public MqttSettings LoadMqtt()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[MqttSectionName];

        if (section is null)
            return new MqttSettings();

        return new MqttSettings
        {
            BrokerAddress = section["BrokerAddress"]?.GetValue<string>() ?? "localhost",
            BrokerPort = section["BrokerPort"]?.GetValue<int>() ?? 1883,
            ClientId = section["ClientId"]?.GetValue<string>() ?? "AMR-Client",
            Username = section["Username"]?.GetValue<string>(),
            Password = section["Password"]?.GetValue<string>()
        };
    }

    public ModbusSettings LoadModbus()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[ModbusSectionName];

        if (section is null)
            return new ModbusSettings();

        return new ModbusSettings
        {
            IpAddress = section["IpAddress"]?.GetValue<string>() ?? "127.0.0.1",
            Port = section["Port"]?.GetValue<int>() ?? 5020,
            SlaveId = (byte)(section["SlaveId"]?.GetValue<int>() ?? 1)
        };
    }

    public CobotModbusSettings LoadCobotModbus()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[CobotModbusSectionName];

        if (section is null)
            return new CobotModbusSettings();

        return new CobotModbusSettings
        {
            IpAddress = section["IpAddress"]?.GetValue<string>() ?? "127.0.0.1",
            Port = section["Port"]?.GetValue<int>() ?? 502,
            SlaveId = (byte)(section["SlaveId"]?.GetValue<int>() ?? 1)
        };
    }

    public IoModuleModbusSettings LoadIoModuleModbus()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[IoModuleModbusSectionName];

        if (section is null)
            return new IoModuleModbusSettings();

        return new IoModuleModbusSettings
        {
            IpAddress = section["IpAddress"]?.GetValue<string>() ?? "127.0.0.1",
            Port = section["Port"]?.GetValue<int>() ?? 502,
            SlaveId = (byte)(section["SlaveId"]?.GetValue<int>() ?? 1)
        };
    }

    /// <summary>
    /// 자동 충전 설정 로드 — 섹션이 없으면(최초 실행) AutoChargeSettings 의 기본값(N1001, 20초) 반환.
    /// 사용자가 값을 바꿔 저장한 경우 그 저장값을 그대로 반환한다.
    /// </summary>
    public AutoChargeSettings LoadAutoCharge()
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json);
        var section = doc?[AutoChargeSectionName];

        if (section is null)
            return new AutoChargeSettings();

        var defaults = new AutoChargeSettings();
        return new AutoChargeSettings
        {
            Enabled = section["Enabled"]?.GetValue<bool>() ?? defaults.Enabled,
            IdleTimeoutSeconds = section["IdleTimeoutSeconds"]?.GetValue<int>() ?? defaults.IdleTimeoutSeconds,
            ChargeNodeId = section["ChargeNodeId"]?.GetValue<string>() ?? defaults.ChargeNodeId
        };
    }

    /// <summary>자동 충전 설정 저장 — appsettings.json "AutoChargeSettings" 섹션에 기록</summary>
    public void SaveAutoCharge(AutoChargeSettings settings)
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json)!;

        doc[AutoChargeSectionName] = JsonSerializer.SerializeToNode(settings);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_settingsFilePath, doc.ToJsonString(options));
    }

    public void Save(MqttSettings mqttSettings, ModbusSettings modbusSettings,
        CobotModbusSettings? cobotModbusSettings = null,
        IoModuleModbusSettings? ioModuleModbusSettings = null)
    {
        var json = File.ReadAllText(_settingsFilePath);
        var doc = JsonNode.Parse(json)!;

        doc[MqttSectionName] = JsonSerializer.SerializeToNode(mqttSettings);
        doc[ModbusSectionName] = JsonSerializer.SerializeToNode(modbusSettings);

        if (cobotModbusSettings != null)
            doc[CobotModbusSectionName] = JsonSerializer.SerializeToNode(cobotModbusSettings);

        if (ioModuleModbusSettings != null)
            doc[IoModuleModbusSectionName] = JsonSerializer.SerializeToNode(ioModuleModbusSettings);

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_settingsFilePath, doc.ToJsonString(options));
    }
}
