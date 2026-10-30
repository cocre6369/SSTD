using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SSTD.Core;

namespace SSTD.Memory;
public sealed class NewDocumentWriter : INewDocumentWriter
{
    public async Task WriteAsync(string path, string content, CancellationToken ct)
    {
        if (content.Length is 0 or > 32000 || SensitiveData.ContainsSecret(content)) throw new InvalidOperationException("Document content is empty, too large, or sensitive.");
        if (File.Exists(path) || Directory.Exists(path)) throw new IOException("The destination exists. No existing file was changed.");
        // CreateNew is atomic on both NTFS and the test filesystem. A competing writer causes failure, never replacement.
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        try
        {
            if (Path.GetExtension(path).Equals(".docx", StringComparison.OrdinalIgnoreCase)) WriteDocx(file, content);
            else await file.WriteAsync(Encoding.UTF8.GetBytes(content), ct).ConfigureAwait(false);
            await file.FlushAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Only the incomplete file opened exclusively by this invocation may be discarded.
            await file.DisposeAsync();
            try { File.Delete(path); } catch (IOException) { }
            throw;
        }
    }
    private static void WriteDocx(Stream stream, string content)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var paragraphs = content.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            var heading = line.StartsWith("# ", StringComparison.Ordinal);
            var text = heading ? line[2..] : line;
            return new XElement(w + "p", heading ? new XElement(w + "pPr", new XElement(w + "pStyle", new XAttribute(w + "val", "Heading1"))) : null,
                new XElement(w + "r", new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
        });
        var document = new XDocument(new XElement(w + "document", new XElement(w + "body", paragraphs,
            new XElement(w + "sectPr", new XElement(w + "pgSz", new XAttribute(w + "w", 12240), new XAttribute(w + "h", 15840))))));
        var styles = new XDocument(new XElement(w + "styles", new XElement(w + "style", new XAttribute(w + "type", "paragraph"), new XAttribute(w + "styleId", "Heading1"),
            new XElement(w + "name", new XAttribute(w + "val", "heading 1")), new XElement(w + "rPr", new XElement(w + "b"), new XElement(w + "sz", new XAttribute(w + "val", 32))))));
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, true);
        void Part(string name, XDocument xml) { var entry = zip.CreateEntry(name); using var s = entry.Open(); xml.Save(s); }
        Part("[Content_Types].xml", new XDocument(new XElement("Types",
            new XElement("Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement("Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement("Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement("Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml")))));
        Part("_rels/.rels", new XDocument(new XElement("Relationships", new XElement("Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "word/document.xml")))));
        Part("word/_rels/document.xml.rels", new XDocument(new XElement("Relationships", new XElement("Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml")))));
        Part("word/document.xml", document); Part("word/styles.xml", styles);
    }
}
