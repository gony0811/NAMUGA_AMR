using System.ComponentModel.DataAnnotations;

namespace AMR.Web.Models;

public class IoModuleModbusSettings
{
    [Required(ErrorMessage = "I/O 모듈 IP 주소를 입력해주세요.")]
    [Display(Name = "I/O 모듈 IP 주소")]
    public string IpAddress { get; set; } = "127.0.0.1";

    [Required]
    [Range(1, 65535)]
    [Display(Name = "I/O 모듈 포트")]
    public int Port { get; set; } = 502;

    [Required]
    [Range(1, 247)]
    [Display(Name = "I/O 모듈 Slave ID")]
    public byte SlaveId { get; set; } = 1;
}
