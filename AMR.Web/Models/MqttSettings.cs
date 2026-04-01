using System.ComponentModel.DataAnnotations;

namespace AMR.Web.Models;

public class MqttSettings
{
    [Required(ErrorMessage = "브로커 주소를 입력해주세요.")]
    [Display(Name = "브로커 주소")]
    public string BrokerAddress { get; set; } = "localhost";

    [Required(ErrorMessage = "포트를 입력해주세요.")]
    [Range(1, 65535, ErrorMessage = "포트는 1~65535 범위여야 합니다.")]
    [Display(Name = "포트")]
    public int BrokerPort { get; set; } = 1883;

    [Required(ErrorMessage = "Client ID를 입력해주세요.")]
    [Display(Name = "Client ID")]
    public string ClientId { get; set; } = "AMR-Client";

    [Display(Name = "사용자명")]
    public string? Username { get; set; }

    [Display(Name = "비밀번호")]
    public string? Password { get; set; }
}
