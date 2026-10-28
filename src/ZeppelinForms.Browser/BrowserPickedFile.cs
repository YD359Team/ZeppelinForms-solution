using ZeppelinForms.Forms.Dialogs;

namespace ZeppelinForms.Browser;

/// <summary>
/// A file the page was given or is to give: its content in /uploads of the virtual FS,
/// and no path of the user's. Read from there; written there too, and on to the user
/// when the stream is disposed — through the handle of the browser's save dialog, or as
/// a download.
/// </summary>
internal sealed class BrowserPickedFile(string name, string storage, int id) : PickedFile(name, path: null)
{
    public override Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(File.OpenRead(storage));

    public override Task<Stream> OpenWriteAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new HandOverStream(this));

    /// <summary>The content written, kept in /uploads for a later read and handed to the script.</summary>
    private Task HandOverAsync(byte[] content)
    {
        File.WriteAllBytes(storage, content);

        return Interop.WritePickedFileAsync(id, Name, Convert.ToBase64String(content));
    }

    /// <summary>Collects what is written; disposed — hands it over. DisposeAsync waits for
    /// the browser to write it; Dispose doesn't — the browser writes it all the same.</summary>
    private sealed class HandOverStream(BrowserPickedFile file) : MemoryStream
    {
        private bool _handedOver;

        public override async ValueTask DisposeAsync()
        {
            if (!_handedOver)
            {
                _handedOver = true;
                await file.HandOverAsync(ToArray());
            }

            await base.DisposeAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_handedOver)
            {
                _handedOver = true;

                // nothing can wait here: the write runs on, and a failure of it is the
                // browser's to report
                _ = file.HandOverAsync(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
