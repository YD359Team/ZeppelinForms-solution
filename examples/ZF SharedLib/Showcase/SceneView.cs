using System.Diagnostics;
using System.Numerics;
using ZeppelinForms;
using ZeppelinForms.Animation;
using ZeppelinForms.Drawing;
using ZeppelinForms.Drawing.Primitives;
using ZeppelinForms.Forms.Controls.Base;
using ZeppelinForms.Forms.Enums;
using ZeppelinForms.Input.Keyboard;
using ZeppelinForms.Input.Mouse;
using ZeppelinForms.Input.Pointer;
using ZeppelinForms.Theming;

namespace ZF_SharedLib.Showcase;

/// <summary>A small 3D viewport drawn with the 2D graphics alone: a cube standing on a
/// ground grid, orbited, panned and zoomed with the mouse as in Blender or Unity, and
/// an axis gizmo in the corner that turns the view to look along an axis.</summary>
/// <remarks>
/// <para>
/// Everything is projected on the CPU and drawn as polygons and lines — no GPU, no
/// depth buffer. A convex cube needs none: of its faces, those turned to the camera
/// never overlap. The grid lies under the cube, so it is drawn first — or last, when
/// the camera looks from below the ground.
/// </para>
/// <para>
/// The world is Y-up, as in Unity; the axes keep Blender's colors. Yaw 0 and pitch 0
/// look at the front of the cube, from +Z.
/// </para>
/// </remarks>
internal sealed class SceneView : InteractiveControl
{
    private const float FieldOfView = 50f * MathF.PI / 180f;
    private const float NearPlane = 0.05f;
    private const float MinDistance = 1.5f;
    private const float MaxDistance = 80f;
    private const float DefaultDistance = 8f;
    private const float DefaultYaw = 35f;
    private const float DefaultPitch = 25f;
    private const int GridExtent = 10;
    private const float OrbitDegreesPerPixel = 0.4f;
    private const float TurntableDegreesPerSecond = 24f;
    private const float FlightSeconds = 0.3f;
    private const float GizmoRadius = 44f;
    private const float GizmoBall = 9f;
    private const string MotionKey = "scene-motion";

    private static readonly Vector3 CubeCenter = new(0, 1, 0);

    // the sun, fixed in the world: the faces change their shade as the view goes round
    private static readonly Vector3 Light = Vector3.Normalize(new Vector3(0.45f, 1f, 0.65f));

    /// <summary>The corners of a 2×2×2 cube standing on the ground.</summary>
    private static readonly Vector3[] Corners =
    [
        new(-1, 0, -1), new(1, 0, -1), new(1, 0, 1), new(-1, 0, 1),
        new(-1, 2, -1), new(1, 2, -1), new(1, 2, 1), new(-1, 2, 1),
    ];

    /// <summary>The faces: the corners round each one, and the way it looks.</summary>
    private static readonly (int[] Corners, Vector3 Normal)[] Faces =
    [
        ([0, 1, 2, 3], -Vector3.UnitY),
        ([4, 7, 6, 5], Vector3.UnitY),
        ([3, 2, 6, 7], Vector3.UnitZ),
        ([0, 4, 5, 1], -Vector3.UnitZ),
        ([1, 5, 6, 2], Vector3.UnitX),
        ([0, 3, 7, 4], -Vector3.UnitX),
    ];

    // Blender's axis colors
    private static readonly Color AxisX = new(255, 0xFF, 0x33, 0x52);
    private static readonly Color AxisY = new(255, 0x8B, 0xDC, 0x00);
    private static readonly Color AxisZ = new(255, 0x28, 0x90, 0xFF);

    private static readonly Color SelectionColor = new(255, 0xFF, 0xA0, 0x33);
    private static readonly Color CubeColor = new(255, 0xD8, 0xDB, 0xE0);
    private static readonly Color EdgeColor = new(210, 0x26, 0x28, 0x2C);
    private static readonly Color GizmoLabelColor = new(255, 0x14, 0x14, 0x14);

    private enum DragKind { None, Orbit, Pan }

    /// <summary>A flight of the camera to a view: a snap to an axis, a framing, a reset.</summary>
    private sealed record Flight(
        float FromYaw, float FromPitch, float FromDistance, Vector3 FromTarget,
        float ToYaw, float ToPitch, float ToDistance, Vector3 ToTarget)
    {
        public float Progress { get; set; }
    }

    /// <summary>The camera of one frame: where it stands, its axes, and the scale of
    /// the projection onto the control.</summary>
    private readonly record struct Camera(
        Vector3 Eye, Vector3 Direction, Vector3 Right, Vector3 Up, Vector3 Forward,
        bool Perspective, float Scale, Point Center)
    {
        /// <summary>A world point in the camera's terms: X right, Y up, Z the depth.</summary>
        public Vector3 ToView(Vector3 point)
        {
            Vector3 d = point - Eye;
            return new Vector3(Vector3.Dot(d, Right), Vector3.Dot(d, Up), Vector3.Dot(d, Forward));
        }

        public Point Project(Vector3 view) => Perspective
            ? new Point(Center.X + view.X * Scale / view.Z, Center.Y - view.Y * Scale / view.Z)
            : new Point(Center.X + view.X * Scale, Center.Y - view.Y * Scale);
    }

    private float _yaw = DefaultYaw;
    private float _pitch = DefaultPitch;
    private float _distance = DefaultDistance;
    private Vector3 _target = CubeCenter;

    private DragKind _drag;
    private Point _last;
    private Point _downAt;
    private bool _moved;
    private long _lastMoveTicks;

    /// <summary>How fast the view was turning when the drag let go, in degrees per
    /// second: it keeps turning and slows down, as a globe flicked with a finger.</summary>
    private float _yawVelocity;
    private float _pitchVelocity;

    private Flight? _flight;
    private bool _ticking;

    // what the last frame drew, for the hit tests between frames
    private readonly List<Point[]> _visibleFaces = [];
    private readonly List<(Point At, float Yaw, float Pitch)> _gizmoBalls = [];
    private Point _gizmoCenter;
    private int _hoveredBall = -1;
    private int _pressedBall = -1;
    private bool _gizmoHovered;

    // the library's own rule, as a control package registers it: every theme the
    // application has gives the viewport its border and corners
    static SceneView() =>
        Theme.RegisterDefault<SceneView>(static (view, theme) =>
        {
            view.BorderColor = theme.Colors.Border;
            view.BorderWidth = theme.Metrics.StrokeThickness;
            view.CornerRadius = theme.Metrics.OverlayCornerRadius;
        });

    public SceneView()
    {
        // a view onto content: it fills its space rather than standing centered
        SetControlDefault(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        SetControlDefault(VerticalAlignmentProperty, VerticalAlignment.Stretch);
    }

    /// <summary>The view changed: it turned, moved, zoomed or switched its projection.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>The turn round the vertical axis, in degrees, -180 to 180.</summary>
    public float Yaw => _yaw;

    /// <summary>The height of the view, in degrees: 90 looks straight down, -90 up.</summary>
    public float Pitch => _pitch;

    /// <summary>How far the camera stands from the point it turns round.</summary>
    public float Distance => _distance;

    /// <summary>Perspective, or orthographic as in a drawing — numpad 5 in Blender.</summary>
    public bool IsPerspective
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            Changed();
        }
    } = true;

    /// <summary>The view goes round the cube by itself.</summary>
    public bool IsTurntable
    {
        get;
        set
        {
            if (field == value) return;

            field = value;

            if (value)
                EnsureMotion();
        }
    }

    /// <summary>The cube is selected: it is outlined in orange, as in Blender.</summary>
    public bool IsSelected
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            InvalidateVisual();
        }
    }

    /// <summary>"Front Orthographic", "User Perspective" — as Blender names the view.</summary>
    public string ViewName
    {
        get
        {
            string side =
                IsAt(0, 0) ? "Front" :
                IsAt(180, 0) ? "Back" :
                IsAt(90, 0) ? "Right" :
                IsAt(-90, 0) ? "Left" :
                _pitch >= 89.99f ? "Top" :
                _pitch <= -89.99f ? "Bottom" :
                "User";

            return $"{side} {(IsPerspective ? "Perspective" : "Orthographic")}";
        }
    }

    private bool IsAt(float yaw, float pitch) =>
        MathF.Abs(Wrap(_yaw - yaw)) < 0.01f && MathF.Abs(_pitch - pitch) < 0.01f;

    // ===== moving the camera =====

    /// <summary>Fly to a view: turned by yaw, raised by pitch, as far and round the same point.</summary>
    public void SnapTo(float yaw, float pitch) => FlyTo(yaw, pitch, _distance, _target);

    /// <summary>Fly back to the view the scene opens with.</summary>
    public void ResetView() => FlyTo(DefaultYaw, DefaultPitch, DefaultDistance, CubeCenter);

    /// <summary>Turn round the cube again and come close to it — after a pan took the view away.</summary>
    public void FrameCube() => FlyTo(_yaw, _pitch, Math.Min(_distance, DefaultDistance), CubeCenter);

    private void FlyTo(float yaw, float pitch, float distance, Vector3 target)
    {
        _yawVelocity = _pitchVelocity = 0;

        // the shorter way round: from 170° to -170° is 20°, not 340°
        float toYaw = _yaw + Wrap(yaw - _yaw);

        _flight = new Flight(_yaw, _pitch, _distance, _target,
            toYaw, Math.Clamp(pitch, -90f, 90f), Math.Clamp(distance, MinDistance, MaxDistance), target);

        // without a form there are no frames: the view goes there at once
        if (FindForm() is null)
        {
            Land(_flight, 1f);
            _flight = null;
            return;
        }

        EnsureMotion();
    }

    private void Land(Flight flight, float t)
    {
        // ease out: a quick start and a soft arrival
        float k = 1f - MathF.Pow(1f - t, 3f);

        _yaw = Wrap(flight.FromYaw + (flight.ToYaw - flight.FromYaw) * k);
        _pitch = flight.FromPitch + (flight.ToPitch - flight.FromPitch) * k;
        _distance = flight.FromDistance + (flight.ToDistance - flight.FromDistance) * k;
        _target = Vector3.Lerp(flight.FromTarget, flight.ToTarget, k);

        // the end lands exactly: the view name and the snapped axis depend on it
        if (t >= 1f)
        {
            _yaw = Wrap(flight.ToYaw);
            _pitch = flight.ToPitch;
        }

        Changed();
    }

    private void Orbit(float yawDegrees, float pitchDegrees)
    {
        _yaw = Wrap(_yaw + yawDegrees);
        _pitch = Math.Clamp(_pitch + pitchDegrees, -90f, 90f);
        Changed();
    }

    /// <summary>Move the point the camera turns round, by pixels of the screen: the
    /// ground under the cursor follows it at the depth of that point.</summary>
    private void Pan(float dx, float dy)
    {
        Rectangle box = LocalBounds;
        if (box.Height <= 0) return;

        Camera camera = CreateCamera(box);
        float unitsPerPixel = _distance * MathF.Tan(FieldOfView / 2f) / (box.Height / 2f);

        _target += (-dx * camera.Right + dy * camera.Up) * unitsPerPixel;
        Changed();
    }

    private void Zoom(float factor)
    {
        float distance = Math.Clamp(_distance * factor, MinDistance, MaxDistance);

        if (distance == _distance) return;

        _distance = distance;
        Changed();
    }

    private void Changed()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private static float Wrap(float degrees) => ((degrees + 180f) % 360f + 360f) % 360f - 180f;

    // ===== frames =====

    /// <summary>Frames while something moves: a flight, a flick slowing down, the turntable.</summary>
    private void EnsureMotion()
    {
        if (_ticking || FindForm() is null) return;

        _ticking = true;
        this.AnimateFrames(MotionKey, Step);
    }

    private bool Step(TimeSpan elapsed)
    {
        float dt = (float)elapsed.TotalSeconds;
        bool busy = false;

        if (_flight is { } flight)
        {
            flight.Progress = Math.Min(1f, flight.Progress + dt / FlightSeconds);
            Land(flight, flight.Progress);

            if (flight.Progress >= 1f)
                _flight = null;

            busy = true;
        }
        else if (_drag == DragKind.None)
        {
            if (MathF.Abs(_yawVelocity) > 2f || MathF.Abs(_pitchVelocity) > 2f)
            {
                Orbit(_yawVelocity * dt, _pitchVelocity * dt);

                float decay = MathF.Exp(-dt * 3.5f);
                _yawVelocity *= decay;
                _pitchVelocity *= decay;

                busy = true;
            }

            if (IsTurntable)
            {
                Orbit(TurntableDegreesPerSecond * dt, 0);
                busy = true;
            }
        }
        else
        {
            // a drag in progress: the turntable waits for it to end
            busy = IsTurntable;
        }

        if (!busy)
            _ticking = false;

        return busy;
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        if (IsTurntable)
            EnsureMotion();
    }

    protected override void OnDetached()
    {
        // the form drops the animations of a subtree that leaves it
        _ticking = false;
        _drag = DragKind.None;
        _flight = null;

        base.OnDetached();
    }

    // ===== drawing =====

    private Camera CreateCamera(Rectangle box)
    {
        float yaw = _yaw * MathF.PI / 180f;
        float pitch = _pitch * MathF.PI / 180f;

        // from the point the camera turns round to the camera
        var direction = new Vector3(
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch),
            MathF.Cos(pitch) * MathF.Cos(yaw));

        // the right of the screen depends on the yaw alone, so straight up or down
        // the camera still knows where its right is — a look-at with the world's up
        // would fall apart there
        var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
        Vector3 forward = -direction;
        Vector3 up = Vector3.Cross(right, forward);

        float half = MathF.Tan(FieldOfView / 2f);
        float scale = IsPerspective
            ? box.Height / 2f / half
            : box.Height / 2f / (_distance * half);

        return new Camera(
            _target + direction * _distance, direction, right, up, forward,
            IsPerspective, scale,
            new Point(box.X + box.Width / 2f, box.Y + box.Height / 2f));
    }

    protected override void DrawContent(Graphics g)
    {
        Rectangle box = LocalBounds;
        if (box.Width <= 1 || box.Height <= 1) return;

        ThemeColors colors = App.Theme.Colors;

        g.Save();
        g.ClipRoundRect(box, CornerRadius);

        // the viewport's own backdrop, lighter at the top, as 3D editors have it
        GradientStop[] backdrop = colors.IsDark
            ? [new(new Color(255, 0x3A, 0x3C, 0x41), 0f), new(new Color(255, 0x22, 0x23, 0x27), 1f)]
            : [new(new Color(255, 0xF4, 0xF5, 0xF7), 0f), new(new Color(255, 0xD9, 0xDC, 0xE1), 1f)];

        g.FillGradient(box, CornerRadius, backdrop, 90f);

        Camera camera = CreateCamera(box);

        // the cube stands on the ground: seen from above, the grid is under it; from
        // below, every line of the grid is nearer than the cube and goes over it
        bool fromBelow = camera.Perspective ? camera.Eye.Y < 0 : camera.Direction.Y < 0;

        if (!fromBelow) DrawGrid(g, camera, colors);
        DrawCube(g, camera);
        if (fromBelow) DrawGrid(g, camera, colors);

        DrawGizmo(g, camera, box, colors);

        g.DrawText(ViewName,
            new Rectangle(new Point(box.X + 12f, box.Y + 8f), new Size(Math.Max(0, box.Width - 24f), 20f)),
            colors.TextSecondary, Font.Default.WithSize(12f),
            HorizontalContentAlignment.Left, VerticalContentAlignment.Center);

        g.Restore();
    }

    private static void DrawGrid(Graphics g, Camera camera, ThemeColors colors)
    {
        Color ink = colors.Text;

        for (int i = -GridExtent; i <= GridExtent; i++)
        {
            bool axis = i == 0;
            byte alpha = axis ? (byte)220 : i % 5 == 0 ? (byte)70 : (byte)34;
            float width = axis ? 1.6f : 1f;

            // in pieces of one cell: each piece fades with its distance from the
            // middle, and the grid melts into the backdrop instead of ending in a square
            for (int j = -GridExtent; j < GridExtent; j++)
            {
                float fade = Fade(j + 0.5f, i);

                if (fade <= 0) continue;

                // along X at z = i — the line at z = 0 is the X axis
                Segment(g, camera, new Vector3(j, 0, i), new Vector3(j + 1, 0, i),
                    (axis ? AxisX : ink).WithA((byte)(alpha * fade)), width);

                // along Z at x = i — the line at x = 0 is the Z axis
                Segment(g, camera, new Vector3(i, 0, j), new Vector3(i, 0, j + 1),
                    (axis ? AxisZ : ink).WithA((byte)(alpha * fade)), width);
            }
        }

        static float Fade(float along, float across)
        {
            float d = MathF.Sqrt(along * along + across * across) / GridExtent;
            return Math.Clamp(1f - d * d, 0f, 1f);
        }
    }

    /// <summary>A line of the world, cut where it passes behind the camera: a point
    /// behind it would be projected mirrored, across the whole screen.</summary>
    private static void Segment(Graphics g, Camera camera, Vector3 from, Vector3 to, Color color, float width)
    {
        if (color.A == 0) return;

        Vector3 a = camera.ToView(from);
        Vector3 b = camera.ToView(to);

        if (camera.Perspective)
        {
            if (a.Z < NearPlane && b.Z < NearPlane) return;

            if (a.Z < NearPlane) a = Vector3.Lerp(a, b, (NearPlane - a.Z) / (b.Z - a.Z));
            else if (b.Z < NearPlane) b = Vector3.Lerp(b, a, (NearPlane - b.Z) / (a.Z - b.Z));
        }

        g.DrawLine(camera.Project(a), camera.Project(b), color, width);
    }

    private void DrawCube(Graphics g, Camera camera)
    {
        _visibleFaces.Clear();

        Span<Vector3> view = stackalloc Vector3[Corners.Length];

        for (int i = 0; i < Corners.Length; i++)
            view[i] = camera.ToView(Corners[i]);

        var polygon = new List<Vector3>(8);

        foreach ((int[] corners, Vector3 normal) in Faces)
        {
            // only the faces turned to the camera: of a convex body they never overlap,
            // so no sorting is needed
            Vector3 middle = CubeCenter + normal;

            bool facing = camera.Perspective
                ? Vector3.Dot(normal, camera.Eye - middle) > 0
                : Vector3.Dot(normal, camera.Direction) > 0;

            if (!facing) continue;

            polygon.Clear();

            foreach (int corner in corners)
                polygon.Add(view[corner]);

            if (camera.Perspective)
                ClipNear(polygon);

            if (polygon.Count < 3) continue;

            var points = new Point[polygon.Count];

            for (int i = 0; i < polygon.Count; i++)
                points[i] = camera.Project(polygon[i]);

            // the sun and some light from everywhere, so the face away from it isn't black
            float light = 0.38f + 0.62f * MathF.Max(0f, Vector3.Dot(normal, Light));

            g.FillPolygon(points, Shade(CubeColor, light));
            _visibleFaces.Add(points);
        }

        // the edges over all the faces: an edge two faces share is drawn by both alike
        Color edge = IsSelected ? SelectionColor : EdgeColor;
        float edgeWidth = IsSelected ? 2.2f : 1.2f;

        foreach (Point[] face in _visibleFaces)
        {
            var closed = new Point[face.Length + 1];
            face.CopyTo(closed, 0);
            closed[^1] = face[0];

            g.DrawPolyline(closed, edge, edgeWidth);
        }
    }

    /// <summary>Cut a polygon of view space where it passes the near plane (Sutherland–Hodgman).</summary>
    private static void ClipNear(List<Vector3> polygon)
    {
        if (polygon.TrueForAll(p => p.Z >= NearPlane)) return;

        var clipped = new List<Vector3>(polygon.Count + 2);

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector3 current = polygon[i];
            Vector3 next = polygon[(i + 1) % polygon.Count];

            bool currentIn = current.Z >= NearPlane;
            bool nextIn = next.Z >= NearPlane;

            if (currentIn)
                clipped.Add(current);

            if (currentIn != nextIn)
                clipped.Add(Vector3.Lerp(current, next, (NearPlane - current.Z) / (next.Z - current.Z)));
        }

        polygon.Clear();
        polygon.AddRange(clipped);
    }

    private static Color Shade(Color color, float k) => new(
        color.A,
        (byte)Math.Clamp(color.R * k, 0f, 255f),
        (byte)Math.Clamp(color.G * k, 0f, 255f),
        (byte)Math.Clamp(color.B * k, 0f, 255f));

    /// <summary>The axes as the camera sees them, in the top right corner: the colored
    /// balls are the positive ends, with their letters; a click on a ball looks along it.</summary>
    private void DrawGizmo(Graphics g, Camera camera, Rectangle box, ThemeColors colors)
    {
        _gizmoCenter = new Point(box.Right - GizmoRadius - 12f, box.Y + GizmoRadius + 12f);
        _gizmoBalls.Clear();

        if (_gizmoHovered)
        {
            g.FillEllipse(
                new Rectangle(new Point(_gizmoCenter.X - GizmoRadius, _gizmoCenter.Y - GizmoRadius),
                    new Size(GizmoRadius * 2, GizmoRadius * 2)),
                colors.Text.WithA(22));
        }

        // where each ball sends the camera: +X — to look from the right, +Y — from above
        (Vector3 Axis, Color Color, string Label, float Yaw, float Pitch)[] axes =
        [
            (Vector3.UnitX, AxisX, "X", 90f, 0f),
            (-Vector3.UnitX, AxisX, string.Empty, -90f, 0f),
            (Vector3.UnitY, AxisY, "Y", 0f, 90f),
            (-Vector3.UnitY, AxisY, string.Empty, 0f, -90f),
            (Vector3.UnitZ, AxisZ, "Z", 0f, 0f),
            (-Vector3.UnitZ, AxisZ, string.Empty, 180f, 0f),
        ];

        float reach = GizmoRadius - GizmoBall - 2f;

        // the farthest first, so the nearer balls go over them
        var order = axes
            .Select((axis, index) => (Index: index, Depth: Vector3.Dot(axis.Axis, camera.Forward)))
            .OrderByDescending(item => item.Depth)
            .ToList();

        var tips = new Point[axes.Length];

        for (int i = 0; i < axes.Length; i++)
        {
            Vector3 axis = axes[i].Axis;

            tips[i] = new Point(
                _gizmoCenter.X + Vector3.Dot(axis, camera.Right) * reach,
                _gizmoCenter.Y - Vector3.Dot(axis, camera.Up) * reach);

            _gizmoBalls.Add((tips[i], axes[i].Yaw, axes[i].Pitch));
        }

        Font font = Font.Default.WithSize(11f).Bold();

        foreach ((int index, _) in order)
        {
            (_, Color color, string label, _, _) = axes[index];
            Point tip = tips[index];
            bool positive = label.Length > 0;
            bool hovered = index == _hoveredBall;

            var ball = new Rectangle(new Point(tip.X - GizmoBall, tip.Y - GizmoBall), new Size(GizmoBall * 2, GizmoBall * 2));

            if (positive)
            {
                g.DrawLine(_gizmoCenter, tip, color, 2f);
                g.FillEllipse(ball, color);
                g.DrawText(label, ball, GizmoLabelColor, font, HorizontalContentAlignment.Center, VerticalContentAlignment.Center);
            }
            else
            {
                g.FillEllipse(ball, color.WithA(70));
                g.DrawEllipse(ball, color.WithA(200), 1.2f);
            }

            if (hovered)
                g.DrawEllipse(ball, colors.Text, 1.5f);
        }
    }

    // ===== input =====

    /// <summary>Mouse events come in window coordinates; the scene draws in its own.</summary>
    private Point ToLocal(Point location)
    {
        Point at = GetAbsolutePosition();
        return new Point(location.X - at.X, location.Y - at.Y);
    }

    private int BallAt(Point local)
    {
        // from the end: the nearest balls were drawn last, on top
        int found = -1;
        float best = float.MaxValue;

        for (int i = 0; i < _gizmoBalls.Count; i++)
        {
            float distance = Point.DistanceBetween(local, _gizmoBalls[i].At);

            if (distance <= GizmoBall + 1f && distance < best)
            {
                best = distance;
                found = i;
            }
        }

        return found;
    }

    private bool IsOverCube(Point local)
    {
        foreach (Point[] face in _visibleFaces)
        {
            if (Contains(face, local)) return true;
        }

        return false;
    }

    /// <summary>A point inside a convex polygon: on the same side of every edge.</summary>
    private static bool Contains(Point[] polygon, Point point)
    {
        int sign = 0;

        for (int i = 0; i < polygon.Length; i++)
        {
            Point a = polygon[i];
            Point b = polygon[(i + 1) % polygon.Length];

            float cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            int side = Math.Sign(cross);

            if (side == 0) continue;
            if (sign == 0) sign = side;
            else if (side != sign) return false;
        }

        return true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        Point local = ToLocal(e.Location);

        // a hand on the view stops whatever moved it
        _flight = null;
        _yawVelocity = _pitchVelocity = 0;

        int ball = BallAt(local);

        if (e.Button == MouseButton.Left && ball >= 0)
        {
            _pressedBall = ball;
            e.Handled = true;
            return;
        }

        // the left and the middle button turn the view, as in Blender; Shift or the
        // right button move it
        bool pan = e.Button == MouseButton.Right || (e.Modifiers & KeyModifiers.Shift) != 0;

        _drag = pan ? DragKind.Pan : DragKind.Orbit;
        _last = _downAt = e.Location;
        _moved = false;
        _lastMoveTicks = Stopwatch.GetTimestamp();

        // without capture the drag breaks off as soon as the cursor leaves the window
        CaptureMouse();
        Cursor = pan ? CursorKind.SizeAll : CursorKind.Default;

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        Point local = ToLocal(e.Location);

        if (_drag == DragKind.None)
        {
            UpdateHover(local);
            return;
        }

        float dx = e.Location.X - _last.X;
        float dy = e.Location.Y - _last.Y;
        _last = e.Location;

        // a few pixels of a shaking hand are still a click, not a drag
        if (!_moved && Point.DistanceBetween(e.Location, _downAt) < 3f) return;

        _moved = true;

        long now = Stopwatch.GetTimestamp();
        float dt = (float)Stopwatch.GetElapsedTime(_lastMoveTicks, now).TotalSeconds;
        _lastMoveTicks = now;

        if (_drag == DragKind.Orbit)
        {
            // the mouse to the right turns the cube to the right — the camera goes left;
            // the mouse down raises the camera to look from above
            float yaw = -dx * OrbitDegreesPerPixel;
            float pitch = dy * OrbitDegreesPerPixel;

            Orbit(yaw, pitch);

            if (dt > 0.0005f)
            {
                // smoothed: a single quick event of a fast mouse is not the speed of the hand
                _yawVelocity = Math.Clamp(_yawVelocity * 0.5f + yaw / dt * 0.5f, -720f, 720f);
                _pitchVelocity = Math.Clamp(_pitchVelocity * 0.5f + pitch / dt * 0.5f, -720f, 720f);
            }
        }
        else
        {
            Pan(dx, dy);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        Point local = ToLocal(e.Location);

        if (_pressedBall >= 0)
        {
            int ball = _pressedBall;
            _pressedBall = -1;

            if (BallAt(local) == ball)
                SnapTo(_gizmoBalls[ball].Yaw, _gizmoBalls[ball].Pitch);

            e.Handled = true;
            return;
        }

        if (_drag == DragKind.None) return;

        DragKind drag = _drag;
        _drag = DragKind.None;

        ReleaseMouseCapture();
        Cursor = CursorKind.Default;

        if (!_moved)
        {
            // a click: the cube is selected, the empty space deselects, as in an editor
            if (e.Button == MouseButton.Left)
                IsSelected = IsOverCube(local);

            _yawVelocity = _pitchVelocity = 0;
        }
        else if (drag == DragKind.Orbit && Stopwatch.GetElapsedTime(_lastMoveTicks).TotalMilliseconds < 60 &&
                 MathF.Abs(_yawVelocity) + MathF.Abs(_pitchVelocity) > 40f)
        {
            // let go while still moving: the view keeps turning and slows down
            EnsureMotion();
        }
        else
        {
            _yawVelocity = _pitchVelocity = 0;
        }

        // the turntable waited for the drag
        if (IsTurntable)
            EnsureMotion();

        e.Handled = true;
    }

    /// <summary>The drag was cut off — a capture lost, a touch canceled: the view stays where it is.</summary>
    protected override void OnPointerCanceled(PointerCancelEventArgs e)
    {
        _drag = DragKind.None;
        _pressedBall = -1;
        _yawVelocity = _pitchVelocity = 0;
        Cursor = CursorKind.Default;
    }

    protected override void OnMouseExit(MouseMoveEventArgs e)
    {
        if (_drag != DragKind.None) return;

        _hoveredBall = -1;
        _gizmoHovered = false;
        InvalidateVisual();
    }

    private void UpdateHover(Point local)
    {
        int ball = BallAt(local);
        bool gizmo = Point.DistanceBetween(local, _gizmoCenter) <= GizmoRadius;

        Cursor = ball >= 0 ? CursorKind.Hand : CursorKind.Default;

        if (ball == _hoveredBall && gizmo == _gizmoHovered) return;

        _hoveredBall = ball;
        _gizmoHovered = gizmo;
        InvalidateVisual();
    }

    protected override void OnDoubleClick(MouseClickEventArgs e)
    {
        if (e.Button != MouseButton.Left) return;

        // a double click on the cube brings it back to the middle, as numpad . in Blender
        if (IsOverCube(ToLocal(e.Location)))
        {
            FrameCube();
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // the form delivers the wheel to disabled elements too
        if (!IsEnabled) return;

        // a notch of the wheel is 120; the wheel up comes closer
        Zoom(MathF.Pow(0.87f, e.Delta / 120f));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool opposite = (e.Modifiers & KeyModifiers.Control) != 0;

        // the keys of Blender's numpad, and of the digit row for keyboards without one
        switch (e.Key)
        {
            case Key.NumPad1 or Key.D1: SnapTo(opposite ? 180f : 0f, 0f); break;
            case Key.NumPad3 or Key.D3: SnapTo(opposite ? -90f : 90f, 0f); break;
            case Key.NumPad7 or Key.D7: SnapTo(0f, opposite ? -90f : 90f); break;
            case Key.NumPad5 or Key.D5: IsPerspective = !IsPerspective; break;

            // as dragging that way would turn it
            case Key.NumPad4 or Key.Left: SnapTo(_yaw + 15f, _pitch); break;
            case Key.NumPad6 or Key.Right: SnapTo(_yaw - 15f, _pitch); break;
            case Key.NumPad8 or Key.Up: SnapTo(_yaw, _pitch - 15f); break;
            case Key.NumPad2 or Key.Down: SnapTo(_yaw, _pitch + 15f); break;

            case Key.Add or Key.OemPlus: Zoom(0.8f); break;
            case Key.Subtract or Key.OemMinus: Zoom(1.25f); break;

            case Key.F or Key.Decimal: FrameCube(); break;
            case Key.Home: ResetView(); break;

            default: return;
        }

        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize) =>
        ResolveSize(new Size(480 + Padding.Horizontal, 360 + Padding.Vertical), availableSize);
}