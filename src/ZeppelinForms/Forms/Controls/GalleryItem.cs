using ZeppelinForms.Drawing.Imaging;

namespace ZeppelinForms.Forms.Controls;

/// <summary>Where a picture of a <see cref="GalleryItem"/> is.</summary>
public enum GalleryImageState : byte
{
    /// <summary>Not there, and not asked for yet.</summary>
    Empty,

    /// <summary>Its loader is running.</summary>
    Loading,

    /// <summary>There: given, or loaded.</summary>
    Loaded,

    /// <summary>The loader failed or found nothing.</summary>
    Failed,
}

/// <summary>
/// One picture of an <see cref="ImageGallery"/>: a thumbnail for the grid, the
/// picture itself for the viewer, both either given or loaded when first needed.
/// </summary>
/// <remarks>
/// <para>
/// The grid draws <see cref="Thumbnail"/>, or <see cref="Image"/> scaled down while
/// there is no thumbnail; the viewer draws <see cref="Image"/>, or the thumbnail
/// while the picture is still loading. Either can be left out and loaded instead:
/// <see cref="ThumbnailLoader"/> runs once the item scrolls into view,
/// <see cref="ImageLoader"/> once the viewer gets to it.
/// </para>
/// <para>
/// A loader runs from the UI thread and may finish on any; the picture is put in
/// place on the UI thread. A failed load is not retried by itself:
/// <see cref="Reload"/> asks again.
/// </para>
/// </remarks>
public class GalleryItem
{
    private GalleryImageState _thumbnailLoad;
    private GalleryImageState _imageLoad;

    public GalleryItem()
    {
    }

    public GalleryItem(Image image, string? title = null)
    {
        Image = image;
        Title = title;
    }

    /// <summary>The caption: under the thumbnail when the gallery shows titles, and
    /// in the viewer.</summary>
    public string? Title
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnChanged();
        }
    }

    /// <summary>What the picture shows, for those who can't see it: the screen
    /// reader reads it after the title.</summary>
    public string? Description
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnChanged();
        }
    }

    /// <summary>Anything the application keeps with the item: a file path, a record.</summary>
    public object? Tag { get; set; }

    /// <summary>The small picture for the grid.</summary>
    public Image? Thumbnail
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            OnChanged();
        }
    }

    /// <summary>The picture itself, for the viewer.</summary>
    public Image? Image
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;

            field = value;
            OnChanged();
        }
    }

    /// <summary>Loads the thumbnail once the item scrolls into view.</summary>
    public Func<CancellationToken, Task<Image?>>? ThumbnailLoader { get; set; }

    /// <summary>Loads the picture once the viewer gets to it.</summary>
    public Func<CancellationToken, Task<Image?>>? ImageLoader { get; set; }

    public GalleryImageState ThumbnailState => GridImage is not null ? GalleryImageState.Loaded : _thumbnailLoad;

    public GalleryImageState ImageState => Image is not null ? GalleryImageState.Loaded : _imageLoad;

    /// <summary>A picture, a title or a loading state changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Forget a failed load, so that the next time the item is needed its
    /// loaders run again.</summary>
    public void Reload()
    {
        bool changed = false;

        if (_thumbnailLoad == GalleryImageState.Failed) { _thumbnailLoad = GalleryImageState.Empty; changed = true; }
        if (_imageLoad == GalleryImageState.Failed) { _imageLoad = GalleryImageState.Empty; changed = true; }

        if (changed) OnChanged();
    }

    /// <summary>What the grid draws.</summary>
    internal Image? GridImage => Thumbnail ?? Image;

    /// <summary>What the viewer draws.</summary>
    internal Image? ViewImage => Image ?? Thumbnail;

    internal bool NeedsThumbnail =>
        GridImage is null && ThumbnailLoader is not null && _thumbnailLoad == GalleryImageState.Empty;

    internal bool NeedsImage =>
        Image is null && ImageLoader is not null && _imageLoad == GalleryImageState.Empty;

    /// <summary>Run a loader: the thumbnail's, or the picture's when
    /// <paramref name="full"/>. Started on the UI thread; the result, and
    /// <paramref name="finished"/>, come back on it.</summary>
    internal async void Load(bool full, Form? form, CancellationToken token, Action finished)
    {
        Func<CancellationToken, Task<Image?>>? loader = full ? ImageLoader : ThumbnailLoader;

        if (loader is null)
        {
            finished();
            return;
        }

        int uiThread = Environment.CurrentManagedThreadId;

        SetLoad(full, GalleryImageState.Loading);

        Image? image = null;
        GalleryImageState outcome;

        try
        {
            image = await loader(token).ConfigureAwait(false);
            outcome = image is null ? GalleryImageState.Failed : GalleryImageState.Loaded;
        }
        catch (OperationCanceledException)
        {
            // not a failure: the gallery left the form or let the item go,
            // and it may be asked for again
            outcome = GalleryImageState.Empty;
        }
        catch
        {
            outcome = GalleryImageState.Failed;
        }

        void Apply()
        {
            if (outcome == GalleryImageState.Loaded)
            {
                // the setter reports the change
                SetLoad(full, GalleryImageState.Empty, report: false);

                if (full) Image = image;
                else Thumbnail = image;
            }
            else
            {
                SetLoad(full, outcome);
            }

            finished();
        }

        // a loader that finished at once — a cache, Task.FromResult — is still on
        // the UI thread; anything else goes back to it through the form's queue
        if (Environment.CurrentManagedThreadId == uiThread || form is null)
            Apply();
        else
            form.Invoke(Apply);
    }

    private void SetLoad(bool full, GalleryImageState state, bool report = true)
    {
        if (full)
        {
            if (_imageLoad == state) return;
            _imageLoad = state;
        }
        else
        {
            if (_thumbnailLoad == state) return;
            _thumbnailLoad = state;
        }

        if (report) OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}