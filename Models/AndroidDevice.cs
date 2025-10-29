using System;

namespace AndroidRecoveryTool.Models
{
    public class AndroidDevice
    {
        public string DeviceId { get; set; } = string.Empty;
        public string DeviceName { get; set; } = "Unknown Device";
        public string Status { get; set; } = "Disconnected";
        public string StatusColor { get; set; } = "Gray";
        public bool IsAuthorized { get; set; }
        public DateTime ConnectedAt { get; set; }
    }
}

