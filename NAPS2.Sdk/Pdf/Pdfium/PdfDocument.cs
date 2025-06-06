using System.Runtime.InteropServices;
using System.Text;

namespace NAPS2.Pdf.Pdfium;

// TODO: Use PdfiumException (with a message, defaulting to unknown error code for the property) instead of other exception types
internal class PdfDocument : NativePdfiumObject
{
    public static PdfDocument Load(string path, string? password = null)
    {
        return new PdfDocument(
            Native.FPDF_LoadDocument(ToUtf(path)!, ToUtf(password)),
            PlatformCompat.System.FileReadLock(path));
    }

    public static PdfDocument Load(Stream stream, string? password = null)
    {
        byte[]? buffer = null;
        if (stream is MemoryStream memoryStream)
        {
            try
            {
                buffer = memoryStream.GetBuffer();
            }
            catch (Exception)
            {
                // The buffer might not be exposable
            }
        }
        if (buffer == null)
        {
            memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            buffer = memoryStream.GetBuffer();
        }
        return Load(buffer, (int) stream.Length, password);
    }

    public static PdfDocument Load(byte[] buffer, int length, string? password = null)
    {
        var gcHandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        return new PdfDocument(
            Native.FPDF_LoadMemDocument(gcHandle.AddrOfPinnedObject(), length, ToUtf(password)),
            gcHandle: gcHandle);
    }

    public static PdfDocument Load(IntPtr buffer, int length, string? password = null)
    {
        return new PdfDocument(
            Native.FPDF_LoadMemDocument(buffer, length, ToUtf(password)));
    }

    // TODO: If we upgrade to .NET Framework 4.7 we can use [MarshalAs(UnmanagedType.LPUTF8Str)]
    private static byte[]? ToUtf(string? str)
    {
        if (str == null)
        {
            return null;
        }
        return Encoding.UTF8.GetBytes(str);
    }

    public static PdfDocument CreateNew()
    {
        return new PdfDocument(Native.FPDF_CreateNewDocument());
    }

    private readonly IDisposable? _readLock;
    private GCHandle _gcHandle;

    private PdfDocument(IntPtr handle, IDisposable? readLock = null, GCHandle gcHandle = default) : base(handle)
    {
        _readLock = readLock;
        _gcHandle = gcHandle;
    }

    public int PageCount => Native.FPDF_GetPageCount(Handle);

    public int? Version => Native.FPDF_GetFileVersion(Handle, out int version) ? version : null;

    public PdfPage GetPage(int pageIndex)
    {
        return new PdfPage(Native.FPDF_LoadPage(Handle, pageIndex), this, pageIndex);
    }

    public void DeletePage(int pageIndex)
    {
        Native.FPDFPage_Delete(Handle, pageIndex);
    }

    public PdfPageObject NewImage()
    {
        return new PdfPageObject(Native.FPDFPageObj_NewImageObj(Handle), this, null, true);
    }

    public PdfPageObject NewText(string font, int fontSize)
    {
        return new PdfPageObject(Native.FPDFPageObj_NewTextObj(Handle, font, fontSize), this, null, true);
    }

    public PdfPageObject NewText(PdfFont font, int fontSize)
    {
        return new PdfPageObject(Native.FPDFPageObj_CreateTextObj(Handle, font.Handle, fontSize), this, null, true);
    }

    public PdfPage NewPage(double width, double height)
    {
        return new PdfPage(Native.FPDFPage_New(Handle, int.MaxValue, width, height), this, -1);
    }

    public void ImportPages(PdfDocument sourceDoc, string? pageRange = null, int insertIndex = 0)
    {
        if (!Native.FPDF_ImportPages(Handle, sourceDoc.Handle, pageRange, insertIndex))
        {
            throw new Exception("Could not import PDF pages");
        }
    }

    public void ImportPage(PdfPage page)
    {
        ImportPages(page.Document, (page.PageIndex + 1).ToString(), PageCount);
    }

    public string GetMetaText(string tag)
    {
        var length = Native.FPDF_GetMetaText(Handle, tag, null, (IntPtr) 0);
        var buffer = new byte[(int) length];
        Native.FPDF_GetMetaText(Handle, tag, buffer, length);
        return Encoding.Unicode.GetString(buffer, 0, buffer.Length - 2);
    }

    public PdfFormEnv CreateFormEnv()
    {
        var formInfo = new PdfiumNativeLibrary.FPDF_FormFillInfo { version = 2 };
        var formInfoHandle = GCHandle.Alloc(formInfo, GCHandleType.Pinned);
        var ptr = formInfoHandle.AddrOfPinnedObject();
        return new PdfFormEnv(Native.FPDFDOC_InitFormFillEnvironment(Handle, ptr), formInfoHandle);
    }

    public PdfFont LoadFont(byte[] data)
    {
        return new PdfFont(Native.FPDFText_LoadFont(Handle, data, data.Length, PdfiumNativeLibrary.FPDF_FONT_TRUETYPE,
            true));
    }

    public void Save(string path)
    {
        using var stream = new FileStream(path, FileMode.Create);
        Save(stream);
    }

    public void Save(Stream stream)
    {
        int WriteBlock(IntPtr self, IntPtr data, IntPtr size)
        {
            var buffer = new byte[(int) size];
            Marshal.Copy(data, buffer, 0, (int) size);
            stream.Write(buffer, 0, (int) size);
            return 1;
        }

        PdfiumNativeLibrary.FPDF_FileWrite fileWrite = new()
        {
            version = 1,
            WriteBlock = WriteBlock
        };
        if (!Native.FPDF_SaveAsCopy(Handle, ref fileWrite, PdfiumNativeLibrary.FPDF_NOINCREMENTAL))
        {
            throw new IOException("Failed to save PDF");
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _readLock?.Dispose();
            if (_gcHandle.IsAllocated)
            {
                _gcHandle.Free();
            }
        }
    }

    protected override void DisposeHandle()
    {
        Native.FPDF_CloseDocument(Handle);
    }
}