namespace ZeppelinForms.Gles.Rendering;

/// <summary>
/// The one program everything is drawn with: an uber-shader whose vertices say what shape
/// they cover and how it is painted, so a frame of mixed primitives goes in few draw calls.
/// </summary>
/// <remarks>
/// <para>
/// A vertex carries its device position, its coordinates in the shape's own space, the
/// shape's half-size, stroke and radii, and the paint's parameters. The coverage of rounded
/// rectangles, ellipses and shadows is computed per pixel from their distance fields, with
/// the antialiasing width taken from the derivatives — exact under any scale, rotation and
/// skew, with no tessellation.
/// </para>
/// <para>
/// The kind is <c>shape + 8 × paint</c>. Shapes: 0 — none, the quad itself; 1 — a rounded
/// rectangle; 2 — an ellipse; 3 — a Gaussian-blurred rectangle. Paints: 0 — the color;
/// 1 — a picture times the color; 2 — the color through a coverage mask; 3 — a gradient
/// ramp; 4 — a layer, desaturated and faded; 5 — fractal noise.
/// </para>
/// <para>
/// Up to two clips are analytic, rounded rectangles in device pixels with antialiased
/// edges; the rest are in the stencil. In the stencil pass the shader only discards what
/// is outside.
/// </para>
/// </remarks>
internal static class Shaders
{
    public const int ShapeNone = 0;
    public const int ShapeRoundRect = 1;
    public const int ShapeEllipse = 2;
    public const int ShapeShadow = 3;

    public const int PaintColor = 0;
    public const int PaintImage = 1;
    public const int PaintMask = 2;
    public const int PaintGradient = 3;
    public const int PaintLayer = 4;
    public const int PaintNoise = 5;

    public static float Kind(int shape, int paint) => shape + 8 * paint;

    /// <summary>The <c>#version</c> line: GLSL ES 3.00 for EGL, ANGLE and WebGL2,
    /// GLSL 3.30 core for desktop OpenGL.</summary>
    public static string Header(bool isGles) => isGles
        ? "#version 300 es\nprecision highp float;\nprecision highp int;\n"
        : "#version 330 core\n";

    public const string Vertex = """
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec2 aLocal;
        layout(location = 2) in vec4 aColor;
        layout(location = 3) in vec4 aShape;
        layout(location = 4) in vec4 aRadii;
        layout(location = 5) in vec4 aExtra;

        // the target's size in pixels, and its origin in device pixels: a layer is drawn
        // in the device's coordinates, only shifted
        uniform vec4 uViewport;

        out vec2 vLocal;
        out vec4 vColor;
        flat out vec4 vShape;
        flat out vec4 vRadii;
        out vec4 vExtra;

        void main()
        {
            vec2 ndc = (aPosition - uViewport.zw) / uViewport.xy * 2.0 - 1.0;

            // y goes down on the device, up in GL
            gl_Position = vec4(ndc.x, -ndc.y, 0.0, 1.0);

            vLocal = aLocal;
            vColor = aColor;
            vShape = aShape;
            vRadii = aRadii;
            vExtra = aExtra;
        }
        """;

    public const string Fragment = """
        in vec2 vLocal;
        in vec4 vColor;
        flat in vec4 vShape;
        flat in vec4 vRadii;
        in vec4 vExtra;

        uniform sampler2D uImage;
        uniform sampler2D uMask;
        uniform sampler2D uRamp;

        uniform vec4 uViewport;
        uniform vec4 uClip0Rect;
        uniform vec4 uClip0Radii;
        uniform vec4 uClip1Rect;
        uniform vec4 uClip1Radii;
        uniform float uStencilWrite;

        out vec4 oColor;

        // a rounded rectangle centered at 0 with half-size b, its radii TL, TR, BR, BL;
        // a sharp corner keeps a sharp distance, so a stroke has mitered outer corners
        float roundRectDistance(vec2 p, vec2 b, vec4 r)
        {
            float radius = p.y < 0.0 ? (p.x < 0.0 ? r.x : r.y) : (p.x < 0.0 ? r.w : r.z);
            vec2 q = abs(p) - b + radius;

            if (radius <= 0.0) return max(q.x, q.y);

            return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
        }

        // an approximation of the distance to an ellipse, exact on circles
        float ellipseDistance(vec2 p, vec2 ab)
        {
            float k0 = length(p / ab);
            float k1 = length(p / (ab * ab));

            if (k1 < 1e-6) return -min(ab.x, ab.y);

            return k0 * (k0 - 1.0) / k1;
        }

        vec4 erf4(vec4 x)
        {
            vec4 s = sign(x);
            vec4 a = abs(x);
            x = 1.0 + (0.278393 + (0.230389 + 0.078108 * (a * a)) * a) * a;
            x *= x;
            return s - s / (x * x);
        }

        // a rectangle convolved with a Gaussian: the product of two integrals of it
        float shadowCoverage(vec2 p, vec2 b, float sigma)
        {
            vec4 query = vec4(p + b, p - b) * (0.70710678 / sigma);
            vec4 integral = 0.5 + 0.5 * erf4(query);
            return (integral.x - integral.z) * (integral.y - integral.w);
        }

        float clipCoverage(vec2 p, vec4 rect, vec4 radii)
        {
            vec2 center = (rect.xy + rect.zw) * 0.5;
            vec2 half_ = (rect.zw - rect.xy) * 0.5;
            float d = roundRectDistance(p - center, half_, radii);
            return clamp(0.5 - d, 0.0, 1.0);
        }

        float hash(vec2 p)
        {
            vec3 q = fract(vec3(p.xyx) * 0.1031);
            q += dot(q, q.yzx + 33.33);
            return fract((q.x + q.y) * q.z);
        }

        float valueNoise(vec2 p, float channel)
        {
            vec2 i = floor(p);
            vec2 f = fract(p);
            vec2 u = f * f * (3.0 - 2.0 * f);
            vec2 o = vec2(channel * 37.0, channel * 17.0);

            return mix(
                mix(hash(i + o), hash(i + o + vec2(1.0, 0.0)), u.x),
                mix(hash(i + o + vec2(0.0, 1.0)), hash(i + o + vec2(1.0, 1.0)), u.x),
                u.y);
        }

        float fractalNoise(vec2 p, float channel)
        {
            return valueNoise(p, channel) * 0.6667 + valueNoise(p * 2.0, channel + 4.0) * 0.3333;
        }

        void main()
        {
            int kind = int(vShape.w + 0.5);
            int shape = kind - (kind / 8) * 8;
            int paint = kind / 8;

            float stroke = vShape.z;

            float d = 0.0;
            if (shape == 1) d = roundRectDistance(vLocal, vShape.xy, vRadii);
            else if (shape == 2) d = ellipseDistance(vLocal, vShape.xy);

            if (stroke > 0.0) d = abs(d) - stroke * 0.5;

            // the length of a device pixel in the shape's units, along the distance's
            // gradient: taken outside any branch, as derivatives must be
            float w = max(length(vec2(dFdx(d), dFdy(d))), 1e-5);

            float coverage = 1.0;

            if (shape == 1 || shape == 2)
            {
                coverage = clamp(0.5 - d / w, 0.0, 1.0);

                // a stroke thinner than a pixel covers only its own share of it
                if (stroke > 0.0) coverage = min(coverage, stroke / w);
            }
            else if (shape == 3)
            {
                coverage = shadowCoverage(vLocal, vShape.xy, vRadii.x);
            }

            vec4 color = vColor;

            if (paint == 1)
            {
                color = texture(uImage, vExtra.xy) * vColor;
            }
            else if (paint == 2)
            {
                coverage *= texture(uMask, vExtra.xy).r;
            }
            else if (paint == 3)
            {
                float t = clamp(vExtra.x, 0.0, 1.0);
                color = texture(uRamp, vec2((t * 255.0 + 0.5) / 256.0, vExtra.y)) * vColor.a;
            }
            else if (paint == 4)
            {
                vec4 c = texture(uImage, vExtra.xy);
                float luminance = dot(c.rgb, vec3(0.213, 0.715, 0.072));
                c.rgb = mix(vec3(luminance), c.rgb, vExtra.z);
                color = c * vColor;
            }
            else if (paint == 5)
            {
                vec2 p = vLocal * vExtra.x;
                vec4 n = vec4(fractalNoise(p, 0.0), fractalNoise(p, 1.0), fractalNoise(p, 2.0), fractalNoise(p, 3.0));
                color = vec4(n.rgb * n.a, n.a) * vColor.a;
            }

            vec2 device = vec2(gl_FragCoord.x, uViewport.y - gl_FragCoord.y) + uViewport.zw;
            coverage *= clipCoverage(device, uClip0Rect, uClip0Radii);
            coverage *= clipCoverage(device, uClip1Rect, uClip1Radii);

            if (uStencilWrite > 0.5)
            {
                if (coverage < 0.5) discard;
                oColor = vec4(1.0);
                return;
            }

            oColor = color * coverage;
        }
        """;
}