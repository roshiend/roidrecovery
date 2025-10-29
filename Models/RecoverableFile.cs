using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AndroidRecoveryTool.Models
{
    public class RecoverableFile : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _thumbnailPath = string.Empty;

        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string FileSize { get; set; } = "0 KB";
        public string Path { get; set; } = string.Empty;
        public string RecoveryStatus { get; set; } = "Not Recovered";
        public long SizeInBytes { get; set; }
        public string DevicePath { get; set; } = string.Empty;
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ThumbnailPath
        {
            get => _thumbnailPath;
            set
            {
                if (_thumbnailPath != value)
                {
                    _thumbnailPath = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

