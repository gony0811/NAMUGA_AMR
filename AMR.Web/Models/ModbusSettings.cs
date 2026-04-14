using System.ComponentModel.DataAnnotations;

namespace AMR.Web.Models;

public class ModbusSettings
{
    [Required(ErrorMessage = "IP 주소를 입력해주세요.")]
    [Display(Name = "IP 주소")]
    public string IpAddress { get; set; } = "127.0.0.1";

    [Required(ErrorMessage = "포트를 입력해주세요.")]
    [Range(1, 65535, ErrorMessage = "포트는 1~65535 범위여야 합니다.")]
    [Display(Name = "포트")]
    public int Port { get; set; } = 502;

    [Required(ErrorMessage = "Slave ID를 입력해주세요.")]
    [Range(1, 247, ErrorMessage = "Slave ID는 1~247 범위여야 합니다.")]
    [Display(Name = "Slave ID")]
    public byte SlaveId { get; set; } = 1;
}
