namespace AndroidRecoveryTool.Models
{
    public class RecoverableFile
    {
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string FileSize { get; set; } = "0 KB";
        public string Path { get; set; } = string.Empty;
        public string RecoveryStatus { get; set; } = "Not Recovered";
        public long SizeInBytes { get; set; }
        public string DevicePath { get; set; } = string.Empty;
        public bool IsSelected { get; set; }
    }
}

