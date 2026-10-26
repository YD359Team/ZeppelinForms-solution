using System.Runtime.InteropServices;
using ZeppelinForms.Core;

namespace ZeppelinForms.Gles;

/// <summary>
/// The OpenGL ES 3.0 functions the renderer calls, loaded by name through the platform's
/// loader — eglGetProcAddress, wglGetProcAddress, the browser's WebGL2 glue.
/// </summary>
/// <remarks>
/// <para>
/// One table per context: a function's address may differ between contexts on WGL, and
/// every window has a context of its own. The calls go straight through unmanaged function
/// pointers, no delegates and no marshalling: a frame makes thousands of them.
/// </para>
/// <para>
/// Only what OpenGL ES 3.0 and desktop OpenGL 3.3 core have in common is used, so one
/// renderer serves EGL, ANGLE, WebGL2 and WGL alike; the shaders differ in their
/// <c>#version</c> line only.
/// </para>
/// </remarks>
internal sealed unsafe class Gl
{
    #region Constants

    public const uint GL_NO_ERROR = 0;
    public const uint GL_VENDOR = 0x1F00;
    public const uint GL_RENDERER = 0x1F01;
    public const uint GL_VERSION = 0x1F02;

    public const uint GL_DEPTH_BUFFER_BIT = 0x0100;
    public const uint GL_STENCIL_BUFFER_BIT = 0x0400;
    public const uint GL_COLOR_BUFFER_BIT = 0x4000;

    public const uint GL_TRIANGLES = 0x0004;
    public const uint GL_TRIANGLE_STRIP = 0x0005;

    public const uint GL_ZERO = 0;
    public const uint GL_ONE = 1;
    public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    public const uint GL_ONE_MINUS_SRC_COLOR = 0x0301;

    public const uint GL_BLEND = 0x0BE2;
    public const uint GL_CULL_FACE = 0x0B44;
    public const uint GL_DEPTH_TEST = 0x0B71;
    public const uint GL_STENCIL_TEST = 0x0B90;
    public const uint GL_SCISSOR_TEST = 0x0C11;
    public const uint GL_DITHER = 0x0BD0;

    public const uint GL_NEVER = 0x0200;
    public const uint GL_EQUAL = 0x0202;
    public const uint GL_ALWAYS = 0x0207;
    public const uint GL_KEEP = 0x1E00;
    public const uint GL_INCR = 0x1E02;

    public const uint GL_UNPACK_ALIGNMENT = 0x0CF5;
    public const uint GL_PACK_ALIGNMENT = 0x0D05;
    public const uint GL_UNPACK_ROW_LENGTH = 0x0CF2;
    public const uint GL_MAX_TEXTURE_SIZE = 0x0D33;

    public const uint GL_UNSIGNED_BYTE = 0x1401;
    public const uint GL_UNSIGNED_SHORT = 0x1403;
    public const uint GL_UNSIGNED_INT = 0x1405;
    public const uint GL_FLOAT = 0x1406;

    public const uint GL_RED = 0x1903;
    public const uint GL_RGBA = 0x1908;
    public const uint GL_R8 = 0x8229;
    public const uint GL_RGBA8 = 0x8058;
    public const uint GL_DEPTH24_STENCIL8 = 0x88F0;

    public const uint GL_TEXTURE_2D = 0x0DE1;
    public const uint GL_TEXTURE0 = 0x84C0;
    public const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    public const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    public const uint GL_TEXTURE_WRAP_S = 0x2802;
    public const uint GL_TEXTURE_WRAP_T = 0x2803;
    public const int GL_NEAREST = 0x2600;
    public const int GL_LINEAR = 0x2601;
    public const int GL_LINEAR_MIPMAP_LINEAR = 0x2703;
    public const int GL_REPEAT = 0x2901;
    public const int GL_CLAMP_TO_EDGE = 0x812F;

    public const uint GL_ARRAY_BUFFER = 0x8892;
    public const uint GL_ELEMENT_ARRAY_BUFFER = 0x8893;
    public const uint GL_STREAM_DRAW = 0x88E0;

    public const uint GL_FRAGMENT_SHADER = 0x8B30;
    public const uint GL_VERTEX_SHADER = 0x8B31;
    public const uint GL_COMPILE_STATUS = 0x8B81;
    public const uint GL_LINK_STATUS = 0x8B82;
    public const uint GL_INFO_LOG_LENGTH = 0x8B84;

    public const uint GL_FRAMEBUFFER = 0x8D40;
    public const uint GL_READ_FRAMEBUFFER = 0x8CA8;
    public const uint GL_DRAW_FRAMEBUFFER = 0x8CA9;
    public const uint GL_RENDERBUFFER = 0x8D41;
    public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    public const uint GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
    public const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint GL_FRAMEBUFFER_BINDING = 0x8CA6;

    #endregion

    #region Functions

    public readonly delegate* unmanaged<uint, byte*> GetString;
    public readonly delegate* unmanaged<uint, int*, void> GetIntegerv;
    public readonly delegate* unmanaged<uint> GetError;
    public readonly delegate* unmanaged<void> Flush;

    public readonly delegate* unmanaged<int, int, int, int, void> Viewport;
    public readonly delegate* unmanaged<int, int, int, int, void> Scissor;
    public readonly delegate* unmanaged<uint, void> Enable;
    public readonly delegate* unmanaged<uint, void> Disable;
    public readonly delegate* unmanaged<uint, uint, void> BlendFunc;
    public readonly delegate* unmanaged<float, float, float, float, void> ClearColor;
    public readonly delegate* unmanaged<int, void> ClearStencil;
    public readonly delegate* unmanaged<uint, void> Clear;
    public readonly delegate* unmanaged<byte, byte, byte, byte, void> ColorMask;
    public readonly delegate* unmanaged<uint, int, uint, void> StencilFunc;
    public readonly delegate* unmanaged<uint, uint, uint, void> StencilOp;
    public readonly delegate* unmanaged<uint, void> StencilMask;

    public readonly delegate* unmanaged<uint, uint> CreateShader;
    public readonly delegate* unmanaged<uint, int, byte**, int*, void> ShaderSource;
    public readonly delegate* unmanaged<uint, void> CompileShader;
    public readonly delegate* unmanaged<uint, uint, int*, void> GetShaderiv;
    public readonly delegate* unmanaged<uint, int, int*, byte*, void> GetShaderInfoLog;
    public readonly delegate* unmanaged<uint, void> DeleteShader;
    public readonly delegate* unmanaged<uint> CreateProgram;
    public readonly delegate* unmanaged<uint, uint, void> AttachShader;
    public readonly delegate* unmanaged<uint, void> LinkProgram;
    public readonly delegate* unmanaged<uint, uint, int*, void> GetProgramiv;
    public readonly delegate* unmanaged<uint, int, int*, byte*, void> GetProgramInfoLog;
    public readonly delegate* unmanaged<uint, void> UseProgram;
    public readonly delegate* unmanaged<uint, void> DeleteProgram;
    public readonly delegate* unmanaged<uint, byte*, int> GetUniformLocation;
    public readonly delegate* unmanaged<int, int, void> Uniform1i;
    public readonly delegate* unmanaged<int, float, void> Uniform1f;
    public readonly delegate* unmanaged<int, float, float, float, float, void> Uniform4f;
    public readonly delegate* unmanaged<int, float, float, void> Uniform2f;

    public readonly delegate* unmanaged<int, uint*, void> GenVertexArrays;
    public readonly delegate* unmanaged<uint, void> BindVertexArray;
    public readonly delegate* unmanaged<int, uint*, void> DeleteVertexArrays;
    public readonly delegate* unmanaged<int, uint*, void> GenBuffers;
    public readonly delegate* unmanaged<uint, uint, void> BindBuffer;
    public readonly delegate* unmanaged<uint, nint, void*, uint, void> BufferData;
    public readonly delegate* unmanaged<int, uint*, void> DeleteBuffers;
    public readonly delegate* unmanaged<uint, void> EnableVertexAttribArray;
    public readonly delegate* unmanaged<uint, int, uint, byte, int, nint, void> VertexAttribPointer;
    public readonly delegate* unmanaged<uint, int, uint, nint, void> DrawElements;
    public readonly delegate* unmanaged<uint, int, int, void> DrawArrays;

    public readonly delegate* unmanaged<int, uint*, void> GenTextures;
    public readonly delegate* unmanaged<uint, uint, void> BindTexture;
    public readonly delegate* unmanaged<uint, void> ActiveTexture;
    public readonly delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> TexImage2D;
    public readonly delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> TexSubImage2D;
    public readonly delegate* unmanaged<uint, uint, int, void> TexParameteri;
    public readonly delegate* unmanaged<int, uint*, void> DeleteTextures;
    public readonly delegate* unmanaged<uint, int, void> PixelStorei;
    public readonly delegate* unmanaged<uint, void> GenerateMipmap;
    public readonly delegate* unmanaged<int, int, int, int, uint, uint, void*, void> ReadPixels;

    public readonly delegate* unmanaged<int, uint*, void> GenFramebuffers;
    public readonly delegate* unmanaged<uint, uint, void> BindFramebuffer;
    public readonly delegate* unmanaged<uint, uint, uint, uint, int, void> FramebufferTexture2D;
    public readonly delegate* unmanaged<uint, uint> CheckFramebufferStatus;
    public readonly delegate* unmanaged<int, uint*, void> DeleteFramebuffers;
    public readonly delegate* unmanaged<int, uint*, void> GenRenderbuffers;
    public readonly delegate* unmanaged<uint, uint, void> BindRenderbuffer;
    public readonly delegate* unmanaged<uint, uint, int, int, void> RenderbufferStorage;
    public readonly delegate* unmanaged<uint, uint, uint, uint, void> FramebufferRenderbuffer;
    public readonly delegate* unmanaged<int, uint*, void> DeleteRenderbuffers;
    public readonly delegate* unmanaged<int, int, int, int, int, int, int, int, uint, int, void> BlitFramebuffer;

    #endregion

    public Gl(Func<string, nint> getProcAddress)
    {
        ArgumentNullException.ThrowIfNull(getProcAddress);

        nint Need(string name)
        {
            nint address = getProcAddress(name);

            return address != 0
                ? address
                : throw new ZfPlatformException($"The GL context has no {name}: the GLES backend needs OpenGL ES 3.0.");
        }

        GetString = (delegate* unmanaged<uint, byte*>)Need("glGetString");
        GetIntegerv = (delegate* unmanaged<uint, int*, void>)Need("glGetIntegerv");
        GetError = (delegate* unmanaged<uint>)Need("glGetError");
        Flush = (delegate* unmanaged<void>)Need("glFlush");

        Viewport = (delegate* unmanaged<int, int, int, int, void>)Need("glViewport");
        Scissor = (delegate* unmanaged<int, int, int, int, void>)Need("glScissor");
        Enable = (delegate* unmanaged<uint, void>)Need("glEnable");
        Disable = (delegate* unmanaged<uint, void>)Need("glDisable");
        BlendFunc = (delegate* unmanaged<uint, uint, void>)Need("glBlendFunc");
        ClearColor = (delegate* unmanaged<float, float, float, float, void>)Need("glClearColor");
        ClearStencil = (delegate* unmanaged<int, void>)Need("glClearStencil");
        Clear = (delegate* unmanaged<uint, void>)Need("glClear");
        ColorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)Need("glColorMask");
        StencilFunc = (delegate* unmanaged<uint, int, uint, void>)Need("glStencilFunc");
        StencilOp = (delegate* unmanaged<uint, uint, uint, void>)Need("glStencilOp");
        StencilMask = (delegate* unmanaged<uint, void>)Need("glStencilMask");

        CreateShader = (delegate* unmanaged<uint, uint>)Need("glCreateShader");
        ShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)Need("glShaderSource");
        CompileShader = (delegate* unmanaged<uint, void>)Need("glCompileShader");
        GetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)Need("glGetShaderiv");
        GetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Need("glGetShaderInfoLog");
        DeleteShader = (delegate* unmanaged<uint, void>)Need("glDeleteShader");
        CreateProgram = (delegate* unmanaged<uint>)Need("glCreateProgram");
        AttachShader = (delegate* unmanaged<uint, uint, void>)Need("glAttachShader");
        LinkProgram = (delegate* unmanaged<uint, void>)Need("glLinkProgram");
        GetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)Need("glGetProgramiv");
        GetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Need("glGetProgramInfoLog");
        UseProgram = (delegate* unmanaged<uint, void>)Need("glUseProgram");
        DeleteProgram = (delegate* unmanaged<uint, void>)Need("glDeleteProgram");
        GetUniformLocation = (delegate* unmanaged<uint, byte*, int>)Need("glGetUniformLocation");
        Uniform1i = (delegate* unmanaged<int, int, void>)Need("glUniform1i");
        Uniform1f = (delegate* unmanaged<int, float, void>)Need("glUniform1f");
        Uniform4f = (delegate* unmanaged<int, float, float, float, float, void>)Need("glUniform4f");
        Uniform2f = (delegate* unmanaged<int, float, float, void>)Need("glUniform2f");
        DrawArrays = (delegate* unmanaged<uint, int, int, void>)Need("glDrawArrays");
        BlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, int, void>)Need("glBlitFramebuffer");

        GenVertexArrays = (delegate* unmanaged<int, uint*, void>)Need("glGenVertexArrays");
        BindVertexArray = (delegate* unmanaged<uint, void>)Need("glBindVertexArray");
        DeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)Need("glDeleteVertexArrays");
        GenBuffers = (delegate* unmanaged<int, uint*, void>)Need("glGenBuffers");
        BindBuffer = (delegate* unmanaged<uint, uint, void>)Need("glBindBuffer");
        BufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)Need("glBufferData");
        DeleteBuffers = (delegate* unmanaged<int, uint*, void>)Need("glDeleteBuffers");
        EnableVertexAttribArray = (delegate* unmanaged<uint, void>)Need("glEnableVertexAttribArray");
        VertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, nint, void>)Need("glVertexAttribPointer");
        DrawElements = (delegate* unmanaged<uint, int, uint, nint, void>)Need("glDrawElements");

        GenTextures = (delegate* unmanaged<int, uint*, void>)Need("glGenTextures");
        BindTexture = (delegate* unmanaged<uint, uint, void>)Need("glBindTexture");
        ActiveTexture = (delegate* unmanaged<uint, void>)Need("glActiveTexture");
        TexImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)Need("glTexImage2D");
        TexSubImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)Need("glTexSubImage2D");
        TexParameteri = (delegate* unmanaged<uint, uint, int, void>)Need("glTexParameteri");
        DeleteTextures = (delegate* unmanaged<int, uint*, void>)Need("glDeleteTextures");
        PixelStorei = (delegate* unmanaged<uint, int, void>)Need("glPixelStorei");
        GenerateMipmap = (delegate* unmanaged<uint, void>)Need("glGenerateMipmap");
        ReadPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)Need("glReadPixels");

        GenFramebuffers = (delegate* unmanaged<int, uint*, void>)Need("glGenFramebuffers");
        BindFramebuffer = (delegate* unmanaged<uint, uint, void>)Need("glBindFramebuffer");
        FramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)Need("glFramebufferTexture2D");
        CheckFramebufferStatus = (delegate* unmanaged<uint, uint>)Need("glCheckFramebufferStatus");
        DeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)Need("glDeleteFramebuffers");
        GenRenderbuffers = (delegate* unmanaged<int, uint*, void>)Need("glGenRenderbuffers");
        BindRenderbuffer = (delegate* unmanaged<uint, uint, void>)Need("glBindRenderbuffer");
        RenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)Need("glRenderbufferStorage");
        FramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)Need("glFramebufferRenderbuffer");
        DeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)Need("glDeleteRenderbuffers");
    }

    /// <summary>A string of the context, "" where it has none.</summary>
    public string String(uint name) => Marshal.PtrToStringUTF8((nint)GetString(name)) ?? string.Empty;

    public int Integer(uint name)
    {
        int value = 0;
        GetIntegerv(name, &value);
        return value;
    }

    public uint GenTexture()
    {
        uint id = 0;
        GenTextures(1, &id);
        return id;
    }

    public void DeleteTexture(uint id)
    {
        if (id != 0) DeleteTextures(1, &id);
    }

    public uint GenFramebuffer()
    {
        uint id = 0;
        GenFramebuffers(1, &id);
        return id;
    }

    public void DeleteFramebuffer(uint id)
    {
        if (id != 0) DeleteFramebuffers(1, &id);
    }

    public uint GenRenderbuffer()
    {
        uint id = 0;
        GenRenderbuffers(1, &id);
        return id;
    }

    public void DeleteRenderbuffer(uint id)
    {
        if (id != 0) DeleteRenderbuffers(1, &id);
    }

    public int UniformLocation(uint program, string name)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");

        fixed (byte* p = bytes)
            return GetUniformLocation(program, p);
    }
}