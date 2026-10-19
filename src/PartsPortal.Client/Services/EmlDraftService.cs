using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;

namespace PartsPortal.Services;

/// <summary>
/// Experimental local-only MIME .eml draft to test New Outlook.
/// Creating this file does not send any email or access Graph.
/// </summary>
internal static class EmlDraftService
{
    public static string Create(
        string pdfPath, string to, string cc, string subject, string body)
    {
        if (!File.Exists(pdfPath))
            throw new FileNotFoundException("PDF attachment not found.", pdfPath);

        string toHeader = NormalizeAddresses(to);
        string ccHeader = NormalizeAddresses(cc);

        if (toHeader.Length == 0 && ccHeader.Length == 0)
            throw new InvalidOperationException("An Email To or CC recipient is required.");

        string folder = Path.GetDirectoryName(Path.GetFullPath(pdfPath))!;
        string pdfName = Path.GetFileName(pdfPath);
        string attachmentName = SanitizeFilename(pdfName);
        string emlName = Path.GetFileNameWithoutExtension(pdfName) +
            "_NewOutlook_TEST_" + Guid.NewGuid().ToString("N")[..8] + ".eml";
        string emlPath = Path.Combine(folder, emlName);

        string boundary = "partsportal_" + Guid.NewGuid().ToString("N");
        string subjectHeader = "=?UTF-8?B?" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes(subject ?? "")) +
            "?=";

        // All file bytes are ASCII; UTF-8 text is MIME-base64 encoded.
        // No UTF-8 BOM or locale-specific code page is written.
        using var file = new FileStream(emlPath, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(file, Encoding.ASCII)
        {
            NewLine = "\r\n"
        };

        writer.WriteLine("X-Unsent: 1");
        writer.WriteLine("MIME-Version: 1.0");
        writer.WriteLine("To: " + toHeader);
        if (ccHeader.Length > 0)
            writer.WriteLine("Cc: " + ccHeader);
        writer.WriteLine("Subject: " + subjectHeader);
        writer.WriteLine("Content-Type: multipart/mixed; boundary=\"" + boundary + "\"");
        writer.WriteLine();

        writer.WriteLine("--" + boundary);
        writer.WriteLine("Content-Type: text/plain; charset=\"utf-8\"");
        writer.WriteLine("Content-Transfer-Encoding: base64");
        writer.WriteLine();
        WriteBase64(writer, Encoding.UTF8.GetBytes(body ?? ""));
        writer.WriteLine();

        writer.WriteLine("--" + boundary);
        writer.WriteLine("Content-Type: application/pdf; name=\"" + attachmentName + "\"");
        writer.WriteLine("Content-Disposition: attachment; filename=\"" + attachmentName + "\"");
        writer.WriteLine("Content-Transfer-Encoding: base64");
        writer.WriteLine();
        WriteBase64(writer, File.ReadAllBytes(pdfPath));
        writer.WriteLine();
        writer.WriteLine("--" + boundary + "--");

        return emlPath;
    }

    private static string NormalizeAddresses(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var addresses = new MailAddressCollection();
        addresses.Add(text.Replace(';', ','));
        // Only the parsed addresses are emitted, never raw header input.
        return string.Join(", ", addresses.Select(x => x.Address));
    }

    private static string SanitizeFilename(string name)
    {
        string safe = new string(name.Select(c =>
            c < ' ' || c > '~' || c is '"' or '\\' or ';' ? '_' : c).ToArray());
        return safe.Length > 0 ? safe : "Material_Request.pdf";
    }

    private static void WriteBase64(TextWriter writer, byte[] bytes)
    {
        string encoded = Convert.ToBase64String(bytes);
        for (int i = 0; i < encoded.Length; i += 76)
            writer.WriteLine(encoded.Substring(i, Math.Min(76, encoded.Length - i)));
    }
}
