using System.Text;

namespace GrblPlotter.AutoWriter.Services
{
    // Windows EDIT renders CRLF+LF and CRCRLF as one break, although Text/Lines
    // retain the extra characters. Canonicalize at the editor boundary so the
    // displayed rows and layout rows agree. Canonical CRLFCRLF and Unix LFLF
    // remain intentional blank lines.
    public static class WritingEditorText
    {
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r')
                {
                    if (i + 2 < text.Length && text[i + 1] == '\r' && text[i + 2] == '\n') i++;
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                        // Stray LF following a Windows break is invisible in EDIT.
                        while (i + 1 < text.Length && text[i + 1] == '\n') i++;
                    }
                    result.Append("\r\n");
                }
                else if (c == '\n') result.Append("\r\n");
                else result.Append(c);
            }
            return result.ToString();
        }
    }
}
