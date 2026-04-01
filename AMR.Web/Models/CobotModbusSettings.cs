using System.ComponentModel.DataAnnotations;

namespace AMR.Web.Models;

public class CobotModbusSettings
{
    [Required(ErrorMessage = "Cobot IP 주소를 입력해주세요.")]
    [Display(Name = "Cobot IP 주소")]
    public string IpAddress { get; set; } = "127.0.0.1";

    [Required]
    [Range(1, 65535)]
    [Display(Name = "Cobot 포트")]
    public int Port { get; set; } = 502;

    [Required]
    [Range(1, 247)]
    [Display(Name = "Cobot Slave ID")]
    public byte SlaveId { get; set; } = 1;
}
