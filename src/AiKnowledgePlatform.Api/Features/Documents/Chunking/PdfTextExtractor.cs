using System.Text;
using UglyToad.PdfPig;

namespace AiKnowledgePlatform.Api.Features.Documents.Chunking;

public static class PdfTextExtractor
{
    public static string ExtractText(string pdfPath)
    {
        var text = new StringBuilder();

        using var document = PdfDocument.Open(pdfPath);
        foreach (var page in document.GetPages())
        {
            var pageText = page.Text;
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                text.AppendLine(pageText);
                text.AppendLine();
            }
        }

        return text.ToString().Trim();
    }
}
