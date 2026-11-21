using System.Runtime.InteropServices;
using Nunam.Core.Management;
using Nunam.Indexer.Abstractions;
using SkiaSharp;

namespace Nunam.Indexer.Parsing
{
    /// <summary>
    /// Renders PDF pages with PDFium (from the bblanchon.PDFium packages). PDFium is not thread-safe, so
    /// every call holds one process-wide lock.
    /// </summary>
    public sealed partial class Pdfium : IRenderedPdf
    {
        private static readonly Lock Gate = new();
        private static bool _initialized;

        private readonly GCHandle _data;
        private readonly IntPtr _document;

        private Pdfium(GCHandle data, IntPtr document)
        {
            _data = data;
            _document = document;
        }

        /// <summary>
        /// Opens a document held in memory; <c>null</c> when PDFium can't read it or it needs a password.
        /// </summary>
        public static Pdfium? Open(byte[] pdf)
        {
            lock (Gate)
            {
                if (!_initialized)
                {
                    FPDF_InitLibrary();
                    _initialized = true;
                }

                // PDFium reads from the buffer for as long as the document is open.
                var data = GCHandle.Alloc(pdf, GCHandleType.Pinned);
                var document = FPDF_LoadMemDocument64(data.AddrOfPinnedObject(), (nuint)pdf.Length, null);
                if (document != IntPtr.Zero) return new Pdfium(data, document);

                data.Free();
                return null;
            }
        }

        public int PageCount
        {
            get { lock (Gate) return FPDF_GetPageCount(_document); }
        }

        /// <summary>
        /// Renders a page on white so its longer edge is <paramref name="maxEdge"/> pixels, as PNG. The
        /// size comes from the requested edge, never from the page's own size, which a file can make huge.
        /// </summary>
        public byte[]? RenderPage(int index, int maxEdge)
        {
            lock (Gate)
            {
                var page = FPDF_LoadPage(_document, index);
                if (page == IntPtr.Zero) return null;
                try
                {
                    var (pageWidth, pageHeight) = (FPDF_GetPageWidthF(page), FPDF_GetPageHeightF(page));
                    if (!(pageWidth > 0 && pageHeight > 0)) return null;

                    var scale = maxEdge / Math.Max(pageWidth, pageHeight);
                    var width = Math.Max(1, (int)Math.Round(pageWidth * scale));
                    var height = Math.Max(1, (int)Math.Round(pageHeight * scale));

                    using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                    var target = FPDFBitmap_CreateEx(width, height, FPDFBitmap_BGRA, bitmap.GetPixels(), bitmap.RowBytes);
                    if (target == IntPtr.Zero) return null;
                    try
                    {
                        FPDFBitmap_FillRect(target, 0, 0, width, height, 0xFFFFFFFF);
                        FPDF_RenderPageBitmap(target, page, 0, 0, width, height, 0, FPDF_ANNOT);
                    }
                    finally
                    {
                        FPDFBitmap_Destroy(target);
                    }

                    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    return data?.ToArray();
                }
                finally
                {
                    FPDF_ClosePage(page);
                }
            }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                FPDF_CloseDocument(_document);
                _data.Free();
            }
        }

        private const string Library = "pdfium";
        private const int FPDFBitmap_BGRA = 4;
        private const int FPDF_ANNOT = 0x01;

        [LibraryImport(Library)]
        private static partial void FPDF_InitLibrary();

        [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, string? password);

        [LibraryImport(Library)]
        private static partial void FPDF_CloseDocument(IntPtr document);

        [LibraryImport(Library)]
        private static partial int FPDF_GetPageCount(IntPtr document);

        [LibraryImport(Library)]
        private static partial IntPtr FPDF_LoadPage(IntPtr document, int index);

        [LibraryImport(Library)]
        private static partial void FPDF_ClosePage(IntPtr page);

        [LibraryImport(Library)]
        private static partial float FPDF_GetPageWidthF(IntPtr page);

        [LibraryImport(Library)]
        private static partial float FPDF_GetPageHeightF(IntPtr page);

        [LibraryImport(Library)]
        private static partial IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr buffer, int stride);

        [LibraryImport(Library)]
        private static partial int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

        [LibraryImport(Library)]
        private static partial void FPDFBitmap_Destroy(IntPtr bitmap);

        [LibraryImport(Library)]
        private static partial void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
    }

    /// <summary>The built-in PDF renderer.</summary>
    public sealed class PdfiumRenderer : IPdfRenderer
    {
        public string Name => IndexerProviders.PdfRenderers.Pdfium;
        public bool IsAvailable => true;
        public IRenderedPdf? Open(byte[] pdf) => Pdfium.Open(pdf);
    }
}
