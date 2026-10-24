using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ZeppelinForms.Core;
using ZeppelinForms.Drawing.Imaging;
using ZeppelinForms.Drawing.Primitives;

namespace ZeppelinForms.Gles.Rendering;

/// <summary>
/// The GPU side of one GL context: the program, the buffers, the textures and the batch
/// the frame's quads gather in until a change of state draws them.
/// </summary>
/// <remarks>
/// <para>
/// Every call is made on the thread that made the context, with it current: the platform
/// makes it current before every frame. Textures freed by the collector are only queued by
/// their finalizers and deleted at the next frame, on that thread.
/// </para>
/// <para>
/// A batch is drawn by one <c>glDrawElements</c>: quads only, their indices made once. What
/// ends a batch is a change of the picture texture, the scissor, the analytic clips, the
/// stencil or the blending, and the target — everything else is in the vertices.
/// </para>
/// <para>
/// The effects that read what is drawn — a blurred backdrop, a reflection, a layer put
/// over in a blend mode — copy it out of the target with a blit first, into a texture of
/// the layers' pool; a blur runs on such a texture with a program of its own.
/// </para>
/// </remarks>
internal sealed unsafe class GlesRenderer : IDisposable
{
    /// <summary>Quads per draw call: the 16-bit indices of four vertices each.</summary>
    private const int MaxQuads = 16384;

    private const int MaskAtlasLimit = 2048;
    private const int RampWidth = 256;
    private const int RampRows = 64;

    /// <summary>Layers unused this many frames give their memory back.</summary>
    private const int LayerIdleFrames = 120;

    private readonly Gl _gl;
    private readonly bool _isGles;

    private uint _program;
    private uint _blurProgram;
    private uint _vao;
    private uint _blurVao;
    private uint _vbo;
    private uint _ibo;

    private uint _white;
    private uint _maskAtlas;
    private uint _rampAtlas;
    private uint _glyphAtlas;

    private int _uViewport;
    private int _uClip0Rect, _uClip0Radii, _uClip1Rect, _uClip1Radii;
    private int _uStencilWrite;

    private int _uBlurDestination, _uBlurSource, _uBlurStep, _uBlurRegion, _uBlurSigma, _uBlurRadius, _uBlurClamp;

    private Vertex[] _vertices = new Vertex[1024];
    private int _quadCount;

    private BatchState _pending;
    private bool _hasPending;

    private RenderTarget _target;

    // what the GL state is now, so a flush sets only what changed
    private BatchState _applied;
    private bool _appliedValid;
    private RenderTarget _appliedTarget;

    private readonly ShelfPacker _maskPacker;
    private readonly int _maskAtlasSize;

    private readonly Dictionary<RampKey, int> _ramps = [];
    private readonly byte[] _rampPixels = new byte[RampWidth * 4];

    private readonly ConditionalWeakTable<Image, ImageTexture> _images = [];
    private readonly ConcurrentQueue<uint> _orphans = new();

    private readonly List<LayerTexture> _layers = [];
    private long _frame;

    private const int GlyphAtlasSize = 2048;
    private readonly ShelfPacker _glyphPacker = new(GlyphAtlasSize, GlyphAtlasSize);
    private readonly Dictionary<(int Typeface, ushort Glyph, int Size, int Subpixel), GlyphSlot> _glyphs = [];
    private readonly Text.GlyphRasterizer _glyphRasterizer = new();

    public GlesRenderer(Gl gl, bool isGles)
    {
        _gl = gl;
        _isGles = isGles;

        _maskAtlasSize = Math.Min(MaskAtlasLimit, Math.Max(1024, gl.Integer(Gl.GL_MAX_TEXTURE_SIZE)));
        _maskPacker = new ShelfPacker(_maskAtlasSize, _maskAtlasSize);

        CreateProgram();
        CreateBuffers();
        CreateTextures();
    }

    public Gl Gl => _gl;

    /// <summary>OpenGL ES rather than desktop OpenGL: the shaders' version line.</summary>
    public bool IsGles => _isGles;

    public int MaxTextureSize => _gl.Integer(Gl.GL_MAX_TEXTURE_SIZE);

    #region Setup

    private void CreateProgram()
    {
        _program = Link(Shaders.Vertex, Shaders.Fragment, "drawing");

        _uViewport = _gl.UniformLocation(_program, "uViewport");
        _uClip0Rect = _gl.UniformLocation(_program, "uClip0Rect");
        _uClip0Radii = _gl.UniformLocation(_program, "uClip0Radii");
        _uClip1Rect = _gl.UniformLocation(_program, "uClip1Rect");
        _uClip1Radii = _gl.UniformLocation(_program, "uClip1Radii");
        _uStencilWrite = _gl.UniformLocation(_program, "uStencilWrite");

        _gl.UseProgram(_program);
        _gl.Uniform1i(_gl.UniformLocation(_program, "uImage"), 0);
        _gl.Uniform1i(_gl.UniformLocation(_program, "uMask"), 1);
        _gl.Uniform1i(_gl.UniformLocation(_program, "uRamp"), 2);
        _gl.Uniform1i(_gl.UniformLocation(_program, "uGlyphs"), 3);
        _gl.Uniform1i(_gl.UniformLocation(_program, "uBackdrop"), 4);

        _blurProgram = Link(Shaders.BlurVertex, Shaders.BlurFragment, "blur");

        _uBlurDestination = _gl.UniformLocation(_blurProgram, "uDestination");
        _uBlurSource = _gl.UniformLocation(_blurProgram, "uSource");
        _uBlurStep = _gl.UniformLocation(_blurProgram, "uStep");
        _uBlurRegion = _gl.UniformLocation(_blurProgram, "uRegion");
        _uBlurSigma = _gl.UniformLocation(_blurProgram, "uSigma");
        _uBlurRadius = _gl.UniformLocation(_blurProgram, "uRadius");
        _uBlurClamp = _gl.UniformLocation(_blurProgram, "uClamp");

        _gl.UseProgram(_blurProgram);
        _gl.Uniform1i(_gl.UniformLocation(_blurProgram, "uTexture"), 0);

        _gl.UseProgram(_program);
    }

    private uint Link(string vertexSource, string fragmentSource, string name)
    {
        string header = Shaders.Header(_isGles);

        uint vertex = Compile(Gl.GL_VERTEX_SHADER, header + vertexSource);
        uint fragment = Compile(Gl.GL_FRAGMENT_SHADER, header + fragmentSource);

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertex);
        _gl.AttachShader(program, fragment);
        _gl.LinkProgram(program);

        _gl.DeleteShader(vertex);
        _gl.DeleteShader(fragment);

        int linked = 0;
        _gl.GetProgramiv(program, Gl.GL_LINK_STATUS, &linked);

        if (linked == 0)
        {
            string log = ProgramLog(program);
            _gl.DeleteProgram(program);

            throw new ZfPlatformException($"The GLES backend's {name} program did not link: {log}");
        }

        return program;
    }

    private uint Compile(uint type, string source)
    {
        uint shader = _gl.CreateShader(type);
        byte[] bytes = Encoding.UTF8.GetBytes(source);

        fixed (byte* text = bytes)
        {
            byte* pointer = text;
            int length = bytes.Length;
            _gl.ShaderSource(shader, 1, &pointer, &length);
        }

        _gl.CompileShader(shader);

        int compiled = 0;
        _gl.GetShaderiv(shader, Gl.GL_COMPILE_STATUS, &compiled);

        if (compiled != 0) return shader;

        int logLength = 0;
        _gl.GetShaderiv(shader, Gl.GL_INFO_LOG_LENGTH, &logLength);

        byte[] log = new byte[Math.Max(1, logLength)];
        fixed (byte* p = log) _gl.GetShaderInfoLog(shader, log.Length, null, p);

        _gl.DeleteShader(shader);

        string kind = type == Gl.GL_VERTEX_SHADER ? "vertex" : "fragment";
        throw new ZfPlatformException(
            $"The GLES backend's {kind} shader did not compile: {Encoding.UTF8.GetString(log).TrimEnd('\0')}");
    }

    private string ProgramLog(uint program)
    {
        int logLength = 0;
        _gl.GetProgramiv(program, Gl.GL_INFO_LOG_LENGTH, &logLength);

        byte[] log = new byte[Math.Max(1, logLength)];
        fixed (byte* p = log) _gl.GetProgramInfoLog(program, log.Length, null, p);

        return Encoding.UTF8.GetString(log).TrimEnd('\0');
    }

    private void CreateBuffers()
    {
        uint vao = 0, vbo = 0, ibo = 0;

        _gl.GenVertexArrays(1, &vao);
        _gl.GenBuffers(1, &vbo);
        _gl.GenBuffers(1, &ibo);

        _vao = vao;
        _vbo = vbo;
        _ibo = ibo;

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(Gl.GL_ARRAY_BUFFER, _vbo);

        // the indices of every quad, made once: 0 1 2, 0 2 3
        ushort[] indices = new ushort[MaxQuads * 6];

        for (int q = 0; q < MaxQuads; q++)
        {
            int v = q * 4;
            int i = q * 6;

            indices[i] = (ushort)v;
            indices[i + 1] = (ushort)(v + 1);
            indices[i + 2] = (ushort)(v + 2);
            indices[i + 3] = (ushort)v;
            indices[i + 4] = (ushort)(v + 2);
            indices[i + 5] = (ushort)(v + 3);
        }

        _gl.BindBuffer(Gl.GL_ELEMENT_ARRAY_BUFFER, _ibo);

        fixed (ushort* p = indices)
            _gl.BufferData(Gl.GL_ELEMENT_ARRAY_BUFFER, indices.Length * sizeof(ushort), p, 0x88E4 /* GL_STATIC_DRAW */);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, Gl.GL_FLOAT, 0, Vertex.Size, 0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, Gl.GL_FLOAT, 0, Vertex.Size, 8);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, Gl.GL_UNSIGNED_BYTE, 1, Vertex.Size, 16);
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 4, Gl.GL_FLOAT, 0, Vertex.Size, 20);
        _gl.EnableVertexAttribArray(4);
        _gl.VertexAttribPointer(4, 4, Gl.GL_FLOAT, 0, Vertex.Size, 36);
        _gl.EnableVertexAttribArray(5);
        _gl.VertexAttribPointer(5, 4, Gl.GL_FLOAT, 0, Vertex.Size, 52);

        _gl.BindVertexArray(0);

        // the blur's corners come from the vertex's index: a vertex array with nothing
        // enabled, as desktop OpenGL's core profile wants one bound to draw at all
        uint blurVao = 0;
        _gl.GenVertexArrays(1, &blurVao);
        _blurVao = blurVao;
    }

    private void CreateTextures()
    {
        // unit 0 has a white pixel when no picture is drawn: a sampler must sample something
        _white = _gl.GenTexture();
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _white);
        uint white = 0xFFFFFFFF;
        _gl.TexImage2D(Gl.GL_TEXTURE_2D, 0, (int)Gl.GL_RGBA8, 1, 1, 0, Gl.GL_RGBA, Gl.GL_UNSIGNED_BYTE, &white);
        SetFiltering(Gl.GL_NEAREST, Gl.GL_NEAREST, Gl.GL_CLAMP_TO_EDGE);

        // the masks are placed pixel for pixel: nearest, never blended with a neighbour
        _maskAtlas = _gl.GenTexture();
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _maskAtlas);
        _gl.TexImage2D(Gl.GL_TEXTURE_2D, 0, (int)Gl.GL_R8, _maskAtlasSize, _maskAtlasSize, 0, Gl.GL_RED, Gl.GL_UNSIGNED_BYTE, null);
        SetFiltering(Gl.GL_NEAREST, Gl.GL_NEAREST, Gl.GL_CLAMP_TO_EDGE);

        _rampAtlas = _gl.GenTexture();
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _rampAtlas);
        _gl.TexImage2D(Gl.GL_TEXTURE_2D, 0, (int)Gl.GL_RGBA8, RampWidth, RampRows, 0, Gl.GL_RGBA, Gl.GL_UNSIGNED_BYTE, null);
        SetFiltering(Gl.GL_LINEAR, Gl.GL_LINEAR, Gl.GL_CLAMP_TO_EDGE);

        // glyphs too are placed pixel for pixel, and live from frame to frame
        _glyphAtlas = _gl.GenTexture();
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _glyphAtlas);
        _gl.TexImage2D(Gl.GL_TEXTURE_2D, 0, (int)Gl.GL_R8, GlyphAtlasSize, GlyphAtlasSize, 0, Gl.GL_RED, Gl.GL_UNSIGNED_BYTE, null);
        SetFiltering(Gl.GL_NEAREST, Gl.GL_NEAREST, Gl.GL_CLAMP_TO_EDGE);
    }

    private void SetFiltering(int min, int mag, int wrap)
    {
        _gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_MIN_FILTER, min);
        _gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_MAG_FILTER, mag);
        _gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_WRAP_S, wrap);
        _gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_WRAP_T, wrap);
    }

    #endregion

    #region Frame

    /// <summary>A frame begins on a target: the GL state is set anew, whatever the context
    /// held — a platform's or a toolkit's code may have used it between frames.</summary>
    public void BeginFrame(RenderTarget target)
    {
        _frame++;

        while (_orphans.TryDequeue(out uint texture))
            _gl.DeleteTexture(texture);

        _gl.Disable(Gl.GL_DEPTH_TEST);
        _gl.Disable(Gl.GL_CULL_FACE);
        _gl.Disable(Gl.GL_DITHER);
        _gl.Enable(Gl.GL_SCISSOR_TEST);
        _gl.ColorMask(1, 1, 1, 1);
        _gl.PixelStorei(Gl.GL_UNPACK_ALIGNMENT, 1);
        _gl.PixelStorei(Gl.GL_UNPACK_ROW_LENGTH, 0);

        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(Gl.GL_ARRAY_BUFFER, _vbo);

        _appliedValid = false;
        _hasPending = false;
        _quadCount = 0;

        // the masks of the last frame are drawn: the atlas starts empty
        _maskPacker.Reset();

        _target = target;
        BindTarget(target);
    }

    public void EndFrame()
    {
        Flush();

        // layers idle for a while give their memory back
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            LayerTexture layer = _layers[i];

            if (!layer.InUse && _frame - layer.LastUsed > LayerIdleFrames)
            {
                layer.Delete(_gl);
                _layers.RemoveAt(i);
            }
        }

        _gl.BindVertexArray(0);
    }

    public RenderTarget Target => _target;

    /// <summary>Draw into another target from now on: what was gathered goes to the old one.</summary>
    public void SetTarget(RenderTarget target)
    {
        Flush();

        _target = target;
        BindTarget(target);
    }

    private void BindTarget(RenderTarget target)
    {
        _gl.BindFramebuffer(Gl.GL_FRAMEBUFFER, target.Framebuffer);
        _gl.Viewport(0, 0, target.Width, target.Height);
        _gl.Uniform4f(_uViewport, target.Width, target.Height, target.OriginX, target.OriginY);

        _appliedTarget = target;

        // the scissor is in the target's own pixels: set anew for it
        _appliedValid = false;
    }

    #endregion

    #region Batching

    /// <summary>Room for one quad in a batch of this state; the four vertices go
    /// clockwise from the top left of the shape.</summary>
    public Span<Vertex> AddQuad(BatchState state)
    {
        if (!_hasPending || !_pending.Equals(state) || _quadCount == MaxQuads)
        {
            Flush();

            _pending = state;
            _hasPending = true;
        }

        int needed = (_quadCount + 1) * 4;

        if (_vertices.Length < needed)
            Array.Resize(ref _vertices, Math.Min(MaxQuads * 4, Math.Max(needed, _vertices.Length * 2)));

        Span<Vertex> quad = _vertices.AsSpan(_quadCount * 4, 4);
        _quadCount++;

        return quad;
    }

    /// <summary>Draw what was gathered.</summary>
    public void Flush()
    {
        if (_quadCount == 0 || !_hasPending) return;

        Apply(_pending);

        fixed (Vertex* p = _vertices)
            _gl.BufferData(Gl.GL_ARRAY_BUFFER, _quadCount * 4 * Vertex.Size, p, Gl.GL_STREAM_DRAW);

        _gl.DrawElements(Gl.GL_TRIANGLES, _quadCount * 6, Gl.GL_UNSIGNED_SHORT, 0);

        _quadCount = 0;
    }

    private void Apply(in BatchState state)
    {
        bool all = !_appliedValid;

        if (all || state.Scissor != _applied.Scissor)
        {
            PixelRect s = state.Scissor;
            RenderTarget t = _appliedTarget;

            // GL's scissor counts rows from the bottom of the target
            _gl.Scissor(s.X - t.OriginX, t.Height - (s.Y - t.OriginY + s.Height), Math.Max(0, s.Width), Math.Max(0, s.Height));
        }

        if (all || state.Blend != _applied.Blend)
        {
            switch (state.Blend)
            {
                case BlendKind.Normal:
                    _gl.Enable(Gl.GL_BLEND);
                    _gl.BlendFunc(Gl.GL_ONE, Gl.GL_ONE_MINUS_SRC_ALPHA);
                    break;

                case BlendKind.Screen:
                    _gl.Enable(Gl.GL_BLEND);
                    _gl.BlendFunc(Gl.GL_ONE, Gl.GL_ONE_MINUS_SRC_COLOR);
                    break;

                default:
                    _gl.Disable(Gl.GL_BLEND);
                    break;
            }
        }

        if (all || state.Clip0 != _applied.Clip0)
            SetClip(_uClip0Rect, _uClip0Radii, state.Clip0);

        if (all || state.Clip1 != _applied.Clip1)
            SetClip(_uClip1Rect, _uClip1Radii, state.Clip1);

        if (all || state.Stencil != _applied.Stencil || state.StencilReference != _applied.StencilReference)
        {
            switch (state.Stencil)
            {
                case StencilUse.None:
                    _gl.Disable(Gl.GL_STENCIL_TEST);
                    _gl.ColorMask(1, 1, 1, 1);
                    _gl.Uniform1f(_uStencilWrite, 0);
                    break;

                case StencilUse.Test:
                    _gl.Enable(Gl.GL_STENCIL_TEST);
                    _gl.StencilMask(0);
                    _gl.StencilFunc(Gl.GL_EQUAL, state.StencilReference, 0xFF);
                    _gl.StencilOp(Gl.GL_KEEP, Gl.GL_KEEP, Gl.GL_KEEP);
                    _gl.ColorMask(1, 1, 1, 1);
                    _gl.Uniform1f(_uStencilWrite, 0);
                    break;

                case StencilUse.Write:
                    _gl.Enable(Gl.GL_STENCIL_TEST);
                    _gl.StencilMask(0xFF);
                    _gl.StencilFunc(Gl.GL_EQUAL, state.StencilReference, 0xFF);
                    _gl.StencilOp(Gl.GL_KEEP, Gl.GL_KEEP, Gl.GL_INCR);
                    _gl.ColorMask(0, 0, 0, 0);
                    _gl.Uniform1f(_uStencilWrite, 1);
                    break;
            }
        }

        // the textures every time: an upload in between may have bound others
        _gl.ActiveTexture(Gl.GL_TEXTURE0 + 4);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, state.Backdrop != 0 ? state.Backdrop : _white);
        _gl.ActiveTexture(Gl.GL_TEXTURE0 + 3);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _glyphAtlas);
        _gl.ActiveTexture(Gl.GL_TEXTURE0 + 2);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _rampAtlas);
        _gl.ActiveTexture(Gl.GL_TEXTURE0 + 1);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _maskAtlas);
        _gl.ActiveTexture(Gl.GL_TEXTURE0);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, state.Image != 0 ? state.Image : _white);

        if (state.Image != 0)
        {
            int wrap = state.ImageRepeat ? Gl.GL_REPEAT : Gl.GL_CLAMP_TO_EDGE;
            _gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_WRAP_S, wrap);
            _gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_WRAP_T, wrap);
        }

        _applied = state;
        _appliedValid = true;
    }

    private void SetClip(int rectLocation, int radiiLocation, in ClipShape clip)
    {
        _gl.Uniform4f(rectLocation, clip.Left, clip.Top, clip.Right, clip.Bottom);
        _gl.Uniform4f(radiiLocation, clip.TopLeft, clip.TopRight, clip.BottomRight, clip.BottomLeft);
    }

    /// <summary>Fill the scissor with a color, replacing what is there: the fast clear,
    /// when no clip but the scissor is set.</summary>
    public void Clear(Color color, PixelRect scissor)
    {
        Flush();

        BatchState state = _hasPending ? _pending : default;

        Apply(state with { Scissor = scissor, Stencil = StencilUse.None });

        float a = color.A / 255f;
        _gl.ClearColor(color.R / 255f * a, color.G / 255f * a, color.B / 255f * a, a);
        _gl.Clear(Gl.GL_COLOR_BUFFER_BIT);
    }

    /// <summary>Empty the stencil of the current target: its clips are about to be put in anew.</summary>
    public void ClearStencil()
    {
        Flush();

        RenderTarget t = _target;

        // the whole target: a clip of a smaller scissor would leave old counts around it
        _gl.Scissor(0, 0, t.Width, t.Height);
        _gl.StencilMask(0xFF);
        _gl.ClearStencil(0);
        _gl.Clear(Gl.GL_STENCIL_BUFFER_BIT);

        _appliedValid = false;
    }

    #endregion

    #region Masks

    /// <summary>A place in the mask atlas for a mask of this size, its pixels uploaded; false
    /// when it can't fit even an empty atlas. A full atlas is drawn and emptied first.</summary>
    public bool UploadMask(int width, int height, ReadOnlySpan<byte> coverage, out int x, out int y)
    {
        x = y = 0;

        if (width > _maskAtlasSize - 2 || height > _maskAtlasSize - 2) return false;

        // one pixel apart: no mask ever samples its neighbour
        if (!_maskPacker.TryAdd(width + 1, height + 1, out x, out y))
        {
            // what was drawn with the masks in it goes first
            Flush();
            _maskPacker.Reset();

            if (!_maskPacker.TryAdd(width + 1, height + 1, out x, out y)) return false;
        }

        _gl.ActiveTexture(Gl.GL_TEXTURE0 + 1);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _maskAtlas);

        fixed (byte* p = coverage)
            _gl.TexSubImage2D(Gl.GL_TEXTURE_2D, 0, x, y, width, height, Gl.GL_RED, Gl.GL_UNSIGNED_BYTE, p);

        _gl.ActiveTexture(Gl.GL_TEXTURE0);

        return true;
    }

    public float MaskAtlasSize => _maskAtlasSize;

    #endregion

    #region Gradients

    /// <summary>The row of the ramp atlas a gradient is in, as a texture coordinate; the ramp
    /// is made the first time the stops are seen.</summary>
    public float GradientRow(ReadOnlySpan<GradientStop> stops)
    {
        var key = new RampKey(stops.ToArray());

        if (!_ramps.TryGetValue(key, out int row))
        {
            if (_ramps.Count == RampRows)
            {
                // a frame of more gradients than rows: what used them is drawn first
                Flush();
                _ramps.Clear();
            }

            row = _ramps.Count;
            _ramps[key] = row;

            FillRamp(stops, _rampPixels);

            _gl.ActiveTexture(Gl.GL_TEXTURE0 + 2);
            _gl.BindTexture(Gl.GL_TEXTURE_2D, _rampAtlas);

            fixed (byte* p = _rampPixels)
                _gl.TexSubImage2D(Gl.GL_TEXTURE_2D, 0, 0, row, RampWidth, 1, Gl.GL_RGBA, Gl.GL_UNSIGNED_BYTE, p);

            _gl.ActiveTexture(Gl.GL_TEXTURE0);
        }

        return (row + 0.5f) / RampRows;
    }

    /// <summary>The colors of a gradient at 256 steps, interpolated unpremultiplied and
    /// premultiplied after, as Skia does; positions out of order are pulled up to the one
    /// before, as Skia does too.</summary>
    private static void FillRamp(ReadOnlySpan<GradientStop> stops, byte[] pixels)
    {
        Span<float> offsets = stackalloc float[stops.Length];
        float previous = 0;

        for (int i = 0; i < stops.Length; i++)
        {
            float offset = Math.Clamp(stops[i].Offset, 0f, 1f);
            offsets[i] = previous = Math.Max(previous, offset);
        }

        for (int x = 0; x < RampWidth; x++)
        {
            float t = x / (float)(RampWidth - 1);

            Color color;

            if (stops.Length == 1 || t <= offsets[0]) color = stops[0].Color;
            else if (t >= offsets[^1]) color = stops[^1].Color;
            else
            {
                int i = 1;
                while (i < stops.Length - 1 && offsets[i] < t) i++;

                float span = offsets[i] - offsets[i - 1];
                float f = span <= 0 ? 1 : (t - offsets[i - 1]) / span;

                Color a = stops[i - 1].Color, b = stops[i].Color;

                color = new Color(
                    Lerp(a.A, b.A, f), Lerp(a.R, b.R, f), Lerp(a.G, b.G, f), Lerp(a.B, b.B, f));
            }

            float alpha = color.A / 255f;

            pixels[x * 4] = (byte)(color.R * alpha + 0.5f);
            pixels[x * 4 + 1] = (byte)(color.G * alpha + 0.5f);
            pixels[x * 4 + 2] = (byte)(color.B * alpha + 0.5f);
            pixels[x * 4 + 3] = color.A;
        }

        static byte Lerp(byte a, byte b, float t) => (byte)(a + (b - a) * t + 0.5f);
    }

    private sealed class RampKey(GradientStop[] stops) : IEquatable<RampKey>
    {
        private readonly int _hash = Hash(stops);

        public bool Equals(RampKey? other) => other is not null && stops.AsSpan().SequenceEqual(other.Stops);

        public override bool Equals(object? obj) => Equals(obj as RampKey);

        public override int GetHashCode() => _hash;

        private GradientStop[] Stops => stops;

        private static int Hash(GradientStop[] stops)
        {
            var hash = new HashCode();
            foreach (GradientStop stop in stops) hash.Add(stop);
            return hash.ToHashCode();
        }
    }

    #endregion

    #region Pictures

    /// <summary>The texture of a picture, uploaded the first time it is drawn: with mipmaps,
    /// so a picture drawn small stays smooth. It lives as long as the picture does.</summary>
    public ImageTexture Texture(Image image)
    {
        if (_images.TryGetValue(image, out ImageTexture? cached)) return cached;

        uint id = _gl.GenTexture();

        _gl.ActiveTexture(Gl.GL_TEXTURE0);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, id);

        fixed (byte* p = image.Pixels)
            _gl.TexImage2D(Gl.GL_TEXTURE_2D, 0, (int)Gl.GL_RGBA8, image.Width, image.Height, 0, Gl.GL_RGBA, Gl.GL_UNSIGNED_BYTE, p);

        _gl.GenerateMipmap(Gl.GL_TEXTURE_2D);
        SetFiltering(Gl.GL_LINEAR_MIPMAP_LINEAR, Gl.GL_LINEAR, Gl.GL_CLAMP_TO_EDGE);

        var texture = new ImageTexture(id, image.Width, image.Height, _orphans);
        _images.AddOrUpdate(image, texture);

        // the batch's picture binding is stale now
        _appliedValid = false;

        return texture;
    }

    #endregion

    #region Glyphs

    /// <summary>A glyph's place in the glyph atlas at a size and a subpixel offset,
    /// rasterized the first time it is drawn so. The atlas lives from frame to frame; when
    /// it is full, what was drawn with it goes first, and it starts empty.</summary>
    /// <param name="size">The size in device pixels.</param>
    public GlyphSlot Glyph(Text.Fonts.Typeface typeface, ushort glyph, float size, int subpixel)
    {
        // eighths of a pixel: an animated size doesn't make a glyph every frame
        int quantized = (int)MathF.Round(size * 8f);
        var key = (typeface.Id, glyph, quantized, subpixel);

        if (_glyphs.TryGetValue(key, out GlyphSlot slot)) return slot;

        Text.GlyphBitmap bitmap = _glyphRasterizer.Rasterize(typeface, glyph, quantized / 8f, subpixel);

        if (bitmap.IsEmpty)
        {
            _glyphs[key] = GlyphSlot.Empty;
            return GlyphSlot.Empty;
        }

        if (!_glyphPacker.TryAdd(bitmap.Width + 1, bitmap.Height + 1, out int x, out int y))
        {
            Flush();

            _glyphs.Clear();
            _glyphPacker.Reset();

            if (!_glyphPacker.TryAdd(bitmap.Width + 1, bitmap.Height + 1, out x, out y)) return GlyphSlot.Empty;
        }

        _gl.ActiveTexture(Gl.GL_TEXTURE0 + 3);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, _glyphAtlas);

        fixed (byte* p = bitmap.Coverage)
            _gl.TexSubImage2D(Gl.GL_TEXTURE_2D, 0, x, y, bitmap.Width, bitmap.Height, Gl.GL_RED, Gl.GL_UNSIGNED_BYTE, p);

        _gl.ActiveTexture(Gl.GL_TEXTURE0);

        const float Size = GlyphAtlasSize;

        slot = new GlyphSlot(
            bitmap.Left, bitmap.Top, bitmap.Width, bitmap.Height,
            x / Size, y / Size, (x + bitmap.Width) / Size, (y + bitmap.Height) / Size);

        _glyphs[key] = slot;
        return slot;
    }

    #endregion

    #region Layers

    /// <summary>A texture with a framebuffer and a stencil at least this large, for a layer:
    /// one the last layers gave back if it fits.</summary>
    public LayerTexture AcquireLayer(int width, int height)
    {
        LayerTexture? best = null;

        foreach (LayerTexture layer in _layers)
        {
            if (layer.InUse || layer.Width < width || layer.Height < height) continue;

            if (best is null || layer.Width * layer.Height < best.Width * best.Height)
                best = layer;
        }

        if (best is null)
        {
            // making it binds its framebuffer: what was gathered goes to the target first,
            // and the target is bound back after
            Flush();

            // rounded up, so a layer that grows by a pixel each frame reuses its texture
            int w = (width + 63) / 64 * 64;
            int h = (height + 63) / 64 * 64;

            best = LayerTexture.Create(_gl, w, h);
            _layers.Add(best);

            _gl.BindFramebuffer(Gl.GL_FRAMEBUFFER, _target.Framebuffer);
            _appliedValid = false;
        }

        // a blurred layer scaled up was sampled smoothly; a layer is placed pixel for pixel
        if (best.Smooth) SetSmooth(best, false);

        best.InUse = true;
        best.LastUsed = _frame;

        return best;
    }

    public static void ReleaseLayer(LayerTexture layer) => layer.InUse = false;

    /// <summary>Sample a layer's texture smoothly — a blur made on a smaller copy is drawn
    /// back scaled up — or pixel for pixel.</summary>
    public void SetSmooth(LayerTexture layer, bool smooth)
    {
        if (layer.Smooth == smooth) return;

        // a draw gathered before may sample it still: drawn with the filtering it had
        Flush();

        int filter = smooth ? Gl.GL_LINEAR : Gl.GL_NEAREST;

        _gl.ActiveTexture(Gl.GL_TEXTURE0);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, layer.Texture);
        _gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_MIN_FILTER, filter);
        _gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_MAG_FILTER, filter);

        layer.Smooth = smooth;
        _appliedValid = false;
    }

    #endregion

    #region Copies and blurs

    /// <summary>What is drawn in a part of the current target, copied into a texture of
    /// the layers' pool: into its lower left, GL's rows from the bottom, as a layer is.</summary>
    /// <remarks>The caller releases it with <see cref="ReleaseLayer"/>.</remarks>
    public LayerTexture CopyTarget(PixelRect region)
    {
        // what was gathered is in the picture copied
        Flush();

        LayerTexture copy = AcquireLayer(region.Width, region.Height);
        RenderTarget t = _target;

        int x = region.X - t.OriginX;
        int y = t.Height - (region.Y - t.OriginY + region.Height);

        Blit(t.Framebuffer, x, y, region.Width, region.Height, copy.Framebuffer, region.Width, region.Height, smooth: false);

        BindTarget(t);
        return copy;
    }

    /// <summary>A copy of a framebuffer's part into another's lower left, scaled to the
    /// size given: averaged when smooth, as a halving is.</summary>
    private void Blit(uint from, int x, int y, int width, int height, uint to, int toWidth, int toHeight, bool smooth)
    {
        // a blit is cut by the scissor, like a draw
        _gl.Disable(Gl.GL_SCISSOR_TEST);

        _gl.BindFramebuffer(Gl.GL_READ_FRAMEBUFFER, from);
        _gl.BindFramebuffer(Gl.GL_DRAW_FRAMEBUFFER, to);
        _gl.BlitFramebuffer(
            x, y, x + width, y + height, 0, 0, toWidth, toHeight,
            Gl.GL_COLOR_BUFFER_BIT, smooth ? Gl.GL_LINEAR : Gl.GL_NEAREST);

        _gl.Enable(Gl.GL_SCISSOR_TEST);
    }

    /// <summary>A Gaussian blur of a layer texture's content, its lower-left
    /// <paramref name="width"/> × <paramref name="height"/>, into a texture of the pool.</summary>
    /// <param name="sigma">In the content's pixels.</param>
    /// <param name="clampEdges">Beyond the content its edge goes on, as a backdrop's
    /// does; otherwise there is nothing there, as around a layer's content.</param>
    /// <remarks>
    /// <para>
    /// A sigma above <see cref="Shaders.MaxBlurSigma"/> is blurred on a copy halved as
    /// many times as it takes, and the result is that small: it is drawn back scaled up,
    /// sampled smoothly. The source stays the caller's; the result is released by the
    /// caller too.
    /// </para>
    /// </remarks>
    public BlurredLayer Blur(LayerTexture source, int width, int height, float sigma, bool clampEdges)
    {
        Flush();

        int scale = 1;

        while (sigma / scale > Shaders.MaxBlurSigma && scale < 64 && width / (scale * 2) >= 2 && height / (scale * 2) >= 2)
            scale *= 2;

        LayerTexture current = source;
        bool owned = false;
        int w = width, h = height;

        for (int step = 1; step < scale; step *= 2)
        {
            int halfWidth = (w + 1) / 2, halfHeight = (h + 1) / 2;
            LayerTexture half = AcquireLayer(halfWidth, halfHeight);

            Blit(current.Framebuffer, 0, 0, w, h, half.Framebuffer, halfWidth, halfHeight, smooth: true);

            if (owned) ReleaseLayer(current);

            current = half;
            owned = true;
            w = halfWidth;
            h = halfHeight;
        }

        float scaledSigma = sigma / scale;

        // a texel more each way than the content: the blur beyond it is computed too, so a
        // result drawn back scaled up and sampled smoothly never reaches stale texels
        LayerTexture across = AcquireLayer(w + 1, h + 1);
        BlurPass(current, across, w, h, horizontal: true, scaledSigma, clampEdges);

        if (owned) ReleaseLayer(current);

        LayerTexture result = AcquireLayer(w + 1, h + 1);
        BlurPass(across, result, w, h, horizontal: false, scaledSigma, clampEdges);

        ReleaseLayer(across);

        RestoreState();
        SetSmooth(result, scale > 1);

        return new BlurredLayer(result, w, h);
    }

    /// <summary>One direction of the blur of the content's <paramref name="width"/> ×
    /// <paramref name="height"/>, written a texel wider and higher: what the blur makes
    /// just beyond the content's edge.</summary>
    private void BlurPass(LayerTexture from, LayerTexture to, int width, int height, bool horizontal, float sigma, bool clampEdges)
    {
        float tw = from.Width, th = from.Height;

        _gl.BindFramebuffer(Gl.GL_FRAMEBUFFER, to.Framebuffer);
        _gl.Viewport(0, 0, width + 1, height + 1);

        _gl.Disable(Gl.GL_SCISSOR_TEST);
        _gl.Disable(Gl.GL_STENCIL_TEST);
        _gl.Disable(Gl.GL_BLEND);
        _gl.ColorMask(1, 1, 1, 1);

        _gl.UseProgram(_blurProgram);
        _gl.BindVertexArray(_blurVao);

        _gl.ActiveTexture(Gl.GL_TEXTURE0);
        _gl.BindTexture(Gl.GL_TEXTURE_2D, from.Texture);

        _gl.Uniform4f(_uBlurDestination, -1, -1, 1, 1);
        _gl.Uniform4f(_uBlurSource, 0, 0, (width + 1) / tw, (height + 1) / th);
        _gl.Uniform2f(_uBlurStep, horizontal ? 1f / tw : 0f, horizontal ? 0f : 1f / th);

        if (clampEdges)
            _gl.Uniform4f(_uBlurRegion, 0.5f / tw, 0.5f / th, (width - 0.5f) / tw, (height - 0.5f) / th);
        else
            _gl.Uniform4f(_uBlurRegion, 0, 0, width / tw, height / th);

        _gl.Uniform1f(_uBlurSigma, MathF.Max(sigma, 1e-3f));
        _gl.Uniform1i(_uBlurRadius, Math.Clamp((int)MathF.Ceiling(sigma * 3f), 1, 64));
        _gl.Uniform1i(_uBlurClamp, clampEdges ? 1 : 0);

        _gl.DrawArrays(Gl.GL_TRIANGLE_STRIP, 0, 4);
    }

    /// <summary>The drawing program, its vertices and the target bound again, the rest of
    /// the state set anew at the next draw: after a blur, or after another renderer drew
    /// in this context between this one's draws.</summary>
    public void RestoreState()
    {
        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(Gl.GL_ARRAY_BUFFER, _vbo);
        _gl.Enable(Gl.GL_SCISSOR_TEST);
        _gl.Disable(Gl.GL_DEPTH_TEST);
        _gl.Disable(Gl.GL_CULL_FACE);
        _gl.PixelStorei(Gl.GL_UNPACK_ALIGNMENT, 1);

        BindTarget(_target);
    }

    #endregion

    /// <summary>Read the target's pixels back, top row first, RGBA premultiplied: for tests
    /// and for the eyedropper.</summary>
    public void ReadPixels(int x, int y, int width, int height, Span<byte> rgba)
    {
        Flush();

        RenderTarget t = _target;
        byte[] rows = new byte[width * height * 4];

        _gl.PixelStorei(Gl.GL_PACK_ALIGNMENT, 1);

        fixed (byte* p = rows)
            _gl.ReadPixels(x - t.OriginX, t.Height - (y - t.OriginY + height), width, height, Gl.GL_RGBA, Gl.GL_UNSIGNED_BYTE, p);

        // GL gives the bottom row first
        for (int row = 0; row < height; row++)
            rows.AsSpan((height - 1 - row) * width * 4, width * 4).CopyTo(rgba.Slice(row * width * 4, width * 4));
    }

    public void Dispose()
    {
        foreach (LayerTexture layer in _layers) layer.Delete(_gl);
        _layers.Clear();

        while (_orphans.TryDequeue(out uint texture))
            _gl.DeleteTexture(texture);

        _gl.DeleteTexture(_white);
        _gl.DeleteTexture(_maskAtlas);
        _gl.DeleteTexture(_rampAtlas);
        _gl.DeleteTexture(_glyphAtlas);

        uint vao = _vao, vbo = _vbo, ibo = _ibo, blurVao = _blurVao;
        if (vao != 0) _gl.DeleteVertexArrays(1, &vao);
        if (blurVao != 0) _gl.DeleteVertexArrays(1, &blurVao);
        if (vbo != 0) _gl.DeleteBuffers(1, &vbo);
        if (ibo != 0) _gl.DeleteBuffers(1, &ibo);

        if (_program != 0) _gl.DeleteProgram(_program);
        if (_blurProgram != 0) _gl.DeleteProgram(_blurProgram);

        _vao = _vbo = _ibo = _blurVao = _program = _blurProgram = 0;
    }
}

/// <summary>A picture's texture. The collector frees it with the picture: the finalizer
/// only queues its name, and the renderer deletes it on its thread at the next frame.</summary>
internal sealed class ImageTexture(uint id, int width, int height, ConcurrentQueue<uint> orphans)
{
    public uint Id { get; } = id;
    public int Width { get; } = width;
    public int Height { get; } = height;

    ~ImageTexture() => orphans.Enqueue(Id);
}

/// <summary>A layer's texture, its framebuffer and its stencil, kept for the next layer.</summary>
internal sealed unsafe class LayerTexture
{
    public uint Texture { get; private init; }
    public uint Framebuffer { get; private init; }
    public uint Stencil { get; private init; }
    public int Width { get; private init; }
    public int Height { get; private init; }

    public bool InUse { get; set; }
    public long LastUsed { get; set; }

    /// <summary>Sampled smoothly rather than pixel for pixel: see <see cref="GlesRenderer.SetSmooth"/>.</summary>
    public bool Smooth { get; set; }

    public static LayerTexture Create(Gl gl, int width, int height)
    {
        uint texture = gl.GenTexture();
        gl.ActiveTexture(Gl.GL_TEXTURE0);
        gl.BindTexture(Gl.GL_TEXTURE_2D, texture);
        gl.TexImage2D(Gl.GL_TEXTURE_2D, 0, (int)Gl.GL_RGBA8, width, height, 0, Gl.GL_RGBA, Gl.GL_UNSIGNED_BYTE, null);
        gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_MIN_FILTER, Gl.GL_NEAREST);
        gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_MAG_FILTER, Gl.GL_NEAREST);
        gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_WRAP_S, Gl.GL_CLAMP_TO_EDGE);
        gl.TexParameteri(Gl.GL_TEXTURE_2D, Gl.GL_TEXTURE_WRAP_T, Gl.GL_CLAMP_TO_EDGE);

        uint stencil = gl.GenRenderbuffer();
        gl.BindRenderbuffer(Gl.GL_RENDERBUFFER, stencil);
        gl.RenderbufferStorage(Gl.GL_RENDERBUFFER, Gl.GL_DEPTH24_STENCIL8, width, height);

        uint framebuffer = gl.GenFramebuffer();
        gl.BindFramebuffer(Gl.GL_FRAMEBUFFER, framebuffer);
        gl.FramebufferTexture2D(Gl.GL_FRAMEBUFFER, Gl.GL_COLOR_ATTACHMENT0, Gl.GL_TEXTURE_2D, texture, 0);
        gl.FramebufferRenderbuffer(Gl.GL_FRAMEBUFFER, Gl.GL_DEPTH_STENCIL_ATTACHMENT, Gl.GL_RENDERBUFFER, stencil);

        uint status = gl.CheckFramebufferStatus(Gl.GL_FRAMEBUFFER);

        if (status != Gl.GL_FRAMEBUFFER_COMPLETE)
        {
            gl.DeleteFramebuffer(framebuffer);
            gl.DeleteRenderbuffer(stencil);
            gl.DeleteTexture(texture);

            throw new ZfPlatformException($"A {width}×{height} layer's framebuffer is incomplete (0x{status:X}).");
        }

        return new LayerTexture
        {
            Texture = texture,
            Framebuffer = framebuffer,
            Stencil = stencil,
            Width = width,
            Height = height,
        };
    }

    public void Delete(Gl gl)
    {
        gl.DeleteFramebuffer(Framebuffer);
        gl.DeleteRenderbuffer(Stencil);
        gl.DeleteTexture(Texture);
    }
}

/// <summary>Rectangles packed into an atlas in shelves: rows of the height of the first
/// rectangle in them, filled left to right. Masks of a frame are of few heights, and a
/// shelf a third taller than a mask still takes it.</summary>
internal sealed class ShelfPacker(int width, int height)
{
    private readonly List<(int Y, int Height, int X)> _shelves = [];
    private int _nextY;

    public void Reset()
    {
        _shelves.Clear();
        _nextY = 0;
    }

    public bool TryAdd(int w, int h, out int x, out int y)
    {
        for (int i = 0; i < _shelves.Count; i++)
        {
            (int shelfY, int shelfHeight, int shelfX) = _shelves[i];

            if (h <= shelfHeight && h * 4 >= shelfHeight * 3 - 4 && shelfX + w <= width)
            {
                x = shelfX;
                y = shelfY;
                _shelves[i] = (shelfY, shelfHeight, shelfX + w);
                return true;
            }
        }

        if (_nextY + h <= height && w <= width)
        {
            x = 0;
            y = _nextY;
            _shelves.Add((_nextY, h, w));
            _nextY += h;
            return true;
        }

        x = y = 0;
        return false;
    }
}

/// <summary>A blur's result: a texture of the layers' pool, the blurred content in its
/// lower-left <see cref="Width"/> × <see cref="Height"/> — smaller than the source by the
/// scale it was blurred at.</summary>
internal readonly record struct BlurredLayer(LayerTexture Texture, int Width, int Height);

/// <summary>A glyph in the glyph atlas: its box against the pen's pixel, y down, and its
/// texture coordinates. Empty: nothing to draw — a space, or a glyph too large.</summary>
internal readonly record struct GlyphSlot(int Left, int Top, int Width, int Height, float U0, float V0, float U1, float V1)
{
    public static readonly GlyphSlot Empty = new(0, 0, 0, 0, 0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;
}