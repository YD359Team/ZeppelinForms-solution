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
/// ramp; 4 — a layer, desaturated and faded; 5 — fractal noise; 6 — the color through a
/// glyph of the glyph atlas; 7 — a layer put over a copy of what is under it in a blend
/// mode, the result replacing what is there.
/// </para>
/// <para>
/// Up to two clips are analytic, rounded rectangles in device pixels with antialiased
/// edges; the rest are in the stencil. In the stencil pass the shader only discards what
/// is outside.
/// </para>
/// <para>
/// A second, small program blurs: one direction a pass, a Gaussian of a sigma up to
/// <see cref="MaxBlurSigma"/> texels — a larger one is blurred on a copy scaled down first.
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
    public const int PaintGlyph = 6;
    public const int PaintBlend = 7;

    /// <summary>The largest sigma a blur pass takes, in texels: beyond it the picture is
    /// scaled down first, as Skia's GPU blur does.</summary>
    public const float MaxBlurSigma = 4f;

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
        uniform sampler2D uGlyphs;
        uniform sampler2D uBackdrop;

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

        // ===== blend modes, premultiplied, as Skia computes them =====

        float colorDodge(float s, float sa, float d, float da)
        {
            if (d == 0.0) return s * (1.0 - da);

            float delta = sa - s;
            if (delta == 0.0) return sa * da + s * (1.0 - da) + d * (1.0 - sa);

            delta = min(da, d * sa / delta);
            return delta * sa + s * (1.0 - da) + d * (1.0 - sa);
        }

        float colorBurn(float s, float sa, float d, float da)
        {
            if (da == d) return sa * da + s * (1.0 - da) + d * (1.0 - sa);
            if (s == 0.0) return d * (1.0 - sa);

            float delta = max(0.0, da - (da - d) * sa / s);
            return delta * sa + s * (1.0 - da) + d * (1.0 - sa);
        }

        float hardLight(float s, float sa, float d, float da)
        {
            float c = 2.0 * s <= sa ? 2.0 * s * d : sa * da - 2.0 * (da - d) * (sa - s);
            return c + s * (1.0 - da) + d * (1.0 - sa);
        }

        float softLight(float s, float sa, float d, float da)
        {
            if (da == 0.0) return s;

            if (2.0 * s <= sa)
                return d * d * (sa - 2.0 * s) / da + (1.0 - da) * s + d * (-sa + 2.0 * s + 1.0);

            if (4.0 * d <= da)
            {
                float d2 = d * d, d3 = d2 * d, da2 = da * da, da3 = da2 * da;
                return (da2 * (s - d * (3.0 * sa - 6.0 * s - 1.0)) + 12.0 * da * d2 * (sa - 2.0 * s)
                    - 16.0 * d3 * (sa - 2.0 * s) - da3 * s) / da2;
            }

            return d * (sa - 2.0 * s + 1.0) + s - sqrt(da * d) * (sa - 2.0 * s) - da * s;
        }

        float luminance(vec3 c) { return dot(vec3(0.3, 0.59, 0.11), c); }

        vec3 setLuminance(vec3 hueSat, float alpha, vec3 lumColor)
        {
            vec3 c = hueSat + luminance(lumColor - hueSat);
            float l = luminance(c);
            float lo = min(min(c.r, c.g), c.b);
            float hi = max(max(c.r, c.g), c.b);

            if (lo < 0.0 && l != lo) c = l + (c - l) * l / (l - lo);
            if (hi > alpha && hi != l) c = l + (c - l) * (alpha - l) / (hi - l);

            return c;
        }

        vec3 setSaturation(vec3 hueLum, vec3 satColor)
        {
            float sat = max(max(satColor.r, satColor.g), satColor.b) - min(min(satColor.r, satColor.g), satColor.b);
            float lo = min(min(hueLum.r, hueLum.g), hueLum.b);
            float hi = max(max(hueLum.r, hueLum.g), hueLum.b);

            return hi > lo ? (hueLum - lo) * sat / (hi - lo) : vec3(0.0);
        }

        // the modes in the order of ZeppelinForms.Drawing.BlendMode
        vec4 blend(vec4 s, vec4 d, int mode)
        {
            float sa = s.a, da = d.a;
            float a = sa + da * (1.0 - sa);
            vec3 sc = s.rgb, dc = d.rgb;
            vec3 rest = sc * (1.0 - da) + dc * (1.0 - sa);

            if (mode == 1) return vec4(sc * dc + rest, a);
            if (mode == 2) return vec4(sc + dc - sc * dc, a);
            if (mode == 3)
                return vec4(hardLight(dc.r, da, sc.r, sa), hardLight(dc.g, da, sc.g, sa), hardLight(dc.b, da, sc.b, sa), a);
            if (mode == 4) return vec4(sc + dc - max(sc * da, dc * sa), a);
            if (mode == 5) return vec4(sc + dc - min(sc * da, dc * sa), a);
            if (mode == 6)
                return vec4(colorDodge(sc.r, sa, dc.r, da), colorDodge(sc.g, sa, dc.g, da), colorDodge(sc.b, sa, dc.b, da), a);
            if (mode == 7)
                return vec4(colorBurn(sc.r, sa, dc.r, da), colorBurn(sc.g, sa, dc.g, da), colorBurn(sc.b, sa, dc.b, da), a);
            if (mode == 8)
                return vec4(hardLight(sc.r, sa, dc.r, da), hardLight(sc.g, sa, dc.g, da), hardLight(sc.b, sa, dc.b, da), a);
            if (mode == 9)
                return vec4(softLight(sc.r, sa, dc.r, da), softLight(sc.g, sa, dc.g, da), softLight(sc.b, sa, dc.b, da), a);
            if (mode == 10) return vec4(sc + dc - 2.0 * min(sc * da, dc * sa), a);
            if (mode == 11) return vec4(sc + dc - 2.0 * sc * dc, a);
            if (mode == 12) return min(s + d, vec4(1.0));

            if (mode >= 13 && mode <= 16)
            {
                float alpha = sa * da;
                vec3 sda = sc * da, dsa = dc * sa;
                vec3 c;

                if (mode == 13) c = setLuminance(setSaturation(sda, dsa), alpha, dsa);
                else if (mode == 14) c = setLuminance(setSaturation(dsa, sda), alpha, dsa);
                else if (mode == 15) c = setLuminance(sda, alpha, dsa);
                else c = setLuminance(dsa, alpha, sda);

                return vec4(c + dc - dsa + sc - sda, a);
            }

            if (mode == 17) return s;
            if (mode == 18) return d;
            if (mode == 19) return d + s * (1.0 - da);
            if (mode == 20) return s * da;
            if (mode == 21) return d * sa;
            if (mode == 22) return s * (1.0 - da);
            if (mode == 23) return d * (1.0 - sa);
            if (mode == 24) return vec4(sc * da + dc * (1.0 - sa), da);
            if (mode == 25) return vec4(dc * sa + sc * (1.0 - da), sa);
            if (mode == 26) return s * (1.0 - da) + d * (1.0 - sa);

            return s + d * (1.0 - sa);
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
            vec4 under = vec4(0.0);

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
            else if (paint == 6)
            {
                coverage *= texture(uGlyphs, vExtra.xy).r;
            }
            else if (paint == 5)
            {
                vec2 p = vLocal * vExtra.x;
                vec4 n = vec4(fractalNoise(p, 0.0), fractalNoise(p, 1.0), fractalNoise(p, 2.0), fractalNoise(p, 3.0));
                color = vec4(n.rgb * n.a, n.a) * vColor.a;
            }
            else if (paint == 7)
            {
                // the layer at its opacity, and what is under it: the copy's coordinates
                // are in the local ones, the shape being none
                under = texture(uBackdrop, vLocal);
                color = blend(texture(uImage, vExtra.xy) * vColor.a, under, int(vExtra.z + 0.5));
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

            // a blend replaces what is there: where the clip covers less, what was under
            // it shows through by as much
            oColor = paint == 7 ? mix(under, color, coverage) : color * coverage;
        }
        """;

    /// <summary>The blur's corners from the vertex's index: a strip of four over the
    /// destination, no buffer needed.</summary>
    public const string BlurVertex = """
        // the destination in normalized device coordinates, the source in texture ones
        uniform vec4 uDestination;
        uniform vec4 uSource;

        out vec2 vTexture;

        void main()
        {
            vec2 corner = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));

            gl_Position = vec4(mix(uDestination.xy, uDestination.zw, corner), 0.0, 1.0);
            vTexture = mix(uSource.xy, uSource.zw, corner);
        }
        """;

    /// <summary>One direction of a Gaussian: the taps a texel apart out to three sigmas,
    /// beyond the source's content either its edge repeated or nothing.</summary>
    public const string BlurFragment = """
        in vec2 vTexture;

        uniform sampler2D uTexture;

        // a texel along the pass's direction, in texture coordinates
        uniform vec2 uStep;

        // the content: for clamping, the centers of its edge texels; for transparent
        // edges, its outer edges
        uniform vec4 uRegion;

        uniform float uSigma;
        uniform int uRadius;
        uniform int uClamp;

        out vec4 oColor;

        void main()
        {
            vec4 sum = vec4(0.0);
            float total = 0.0;
            float k = -0.5 / (uSigma * uSigma);

            for (int i = -uRadius; i <= uRadius; i++)
            {
                float w = exp(float(i * i) * k);
                vec2 uv = vTexture + uStep * float(i);

                total += w;

                if (uClamp == 1)
                {
                    uv = clamp(uv, uRegion.xy, uRegion.zw);
                }
                else if (any(lessThan(uv, uRegion.xy)) || any(greaterThan(uv, uRegion.zw)))
                {
                    continue;
                }

                sum += texture(uTexture, uv) * w;
            }

            oColor = sum / total;
        }
        """;
}