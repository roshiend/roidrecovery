using System;
using System.Globalization;
using System.Windows.Data;

namespace AndroidRecoveryTool
{
    public class FileTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string fileType)
            {
                return fileType.ToUpper() switch
                {
                    "MP4" or "AVI" or "MKV" or "MOV" or "M4V" or "FLV" or "WMV" or "3GP" => "🎬",
                    "MP3" or "WAV" or "M4A" or "AAC" or "OGG" => "🎵",
                    "PDF" => "📄",
                    "DOC" or "DOCX" => "📝",
                    "XLS" or "XLSX" => "📊",
                    "TXT" => "📃",
                    "ZIP" => "📦",
                    _ => "📎"
                };
            }
            return "📎";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

