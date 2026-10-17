using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using NiflySharp;
using NiflySharp.Blocks;
using System.Linq;
using NifRenderer.BodySlide;
using NifRenderer.Editing;
using NifRenderer.Resolver;
using NifRenderer.Shader;
using NifRenderer.UI;
using NifRenderer.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using Transform = NifRenderer.Editing.Transform;
using Axis = NifRenderer.Editing.Transform.Axis;
using SceneTransform = NifRenderer.Renderer.Transform;

namespace NifRenderer.Renderer
{
    public class Renderer : IDisposable
    {
        public static bool ShowWireframe;

        private GraphicsDevice _device;

        private Effect _effect;

        private Matrix _viewMatrix;

        private Matrix _projectionMatrix;

        private readonly List<RenderObject> _objects = [];

        private readonly HashSet<RenderShape> _selection = [];

        private RasterizerState _solidState;

        private readonly BlendState _axisBlend = new()
        {
            ColorSourceBlend = Blend.SourceAlpha,
            ColorDestinationBlend = Blend.InverseSourceAlpha,
            AlphaSourceBlend = Blend.One,
            AlphaDestinationBlend = Blend.InverseSourceAlpha,
            ColorWriteChannels = ColorWriteChannels.All
        };

        private const int AxisSegments = 32;

        private const int GridSegments = 8;

        private const int GridCells = 40;

        private const float GridSpacingValue = 5f;

        private const int GridMinCells = 20;

        private const int GridMaxCells = 400;

        private const float GridFadeStart = 1.2f;

        private const float GridFadeEnd = 2.4f;

        private const float LineFadeStart = 0.55f;

        private const float LineFadeEnd = 1.00f;

        private const int AxisThickness = 2;

        private const int GridThickness = 1;

        private struct PanelPoint
        {
            public Vector2 Position;

            public float Depth;

            public float Alpha;
        }

        private void DrawGrid(GraphicsDevice device, Rectangle viewport, Effect effect)
        {
            if (viewport.Width <= 0 || viewport.Height <= 0)
                return;

            var spacing = GridSpacing();

            GridMetrics(viewport, out var cells, out var extent, out var fadeStart, out var fadeEnd);

            if (_gridKeyWidth != viewport.Width || _gridKeyHeight != viewport.Height || _gridKeyView != _viewMatrix)
            {
                _gridKeyWidth = viewport.Width;

                _gridKeyHeight = viewport.Height;

                _gridKeyView = _viewMatrix;

                _gridBuffer.Clear();

                    for (var i = -cells; i <= cells; i++)
                {
                    if (i == 0)
                        continue;

                    var offset = i * spacing;

                    AppendWorldLine(_gridBuffer, viewport, new Vector3(-extent, offset, 0f), new Vector3(extent, offset, 0f), UITheme.GridLine, GridThickness, fadeStart, fadeEnd, GridSegments);

                    AppendWorldLine(_gridBuffer, viewport, new Vector3(offset, -extent, 0f), new Vector3(offset, extent, 0f), UITheme.GridLine, GridThickness, fadeStart, fadeEnd, GridSegments);
                }

                _gridCount = _gridBuffer.Count;

                if (_gridArray.Length < _gridCount)
                    _gridArray = new VertexPositionColor[Math.Max(_gridCount, 1024)];

                _gridBuffer.CopyTo(_gridArray);
            }

            if (_gridCount == 0)
                return;

            device.BlendState = _axisBlend;

            device.DepthStencilState = DepthStencilState.Default;

            device.RasterizerState = _solidState;

            effect.CurrentTechnique = effect.Techniques["Axis"];

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();

                device.DrawUserPrimitives(PrimitiveType.TriangleList, _gridArray, 0, _gridCount / 3, VertexPositionColor.VertexDeclaration);
            }

            device.BlendState = BlendState.Opaque;

            device.DepthStencilState = DepthStencilState.Default;
        }

        private float GridSpacing() => GridSpacingValue;

        private static void BoneShape(Vector3 head, Vector3 tail, Span<Vector3> vertices)
        {
            var axis = tail - head;

            var length = axis.Length();

            if (length < 1e-4f)
                length = 1e-4f;

            var forward = axis / length;

            var reference = MathF.Abs(forward.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;

            var side = Vector3.Normalize(Vector3.Cross(reference, forward));

            var up = Vector3.Cross(forward, side);

            var ring = head + forward * (length * 0.1f);

            var width = length * 0.1f;

            vertices[0] = head;

            vertices[1] = ring + (side + up) * width;

            vertices[2] = ring + (side - up) * width;

            vertices[3] = ring - (side + up) * width;

            vertices[4] = ring - (side - up) * width;

            vertices[5] = tail;
        }

        private void GridMetrics(Rectangle viewport, out int cells, out float extent, out float fadeStart, out float fadeEnd)
        {
            var halfHeight = MathF.Tan(MathHelper.PiOver4 * 0.5f) * _distance;

            var halfWidth = halfHeight * (viewport.Width / (float)Math.Max(1, viewport.Height));

            var view = MathF.Max(halfHeight, halfWidth);

            fadeStart = view * GridFadeStart;

            fadeEnd = view * GridFadeEnd;

            extent = fadeEnd;

            cells = Math.Clamp((int)MathF.Ceiling(extent / GridSpacingValue), GridMinCells, GridMaxCells);
        }

        private void DrawAxes(GraphicsDevice device, Rectangle viewport, Effect effect)
        {
            if (viewport.Width <= 0 || viewport.Height <= 0)
                return;

            GridMetrics(viewport, out _, out var reach, out var fadeStart, out var fadeEnd);

            var axisLock = TransformOperator.Instance.Active ? TransformOperator.Instance.Constraint.LockedAxis : null;

            if (_axisKeyWidth != viewport.Width || _axisKeyHeight != viewport.Height || _axisKeyView != _viewMatrix || _axisKeyLock != axisLock)
            {
                _axisKeyWidth = viewport.Width;

                _axisKeyHeight = viewport.Height;

                _axisKeyView = _viewMatrix;

                _axisKeyLock = axisLock;

                _axisBuffer.Clear();

                AppendWorldLine(_axisBuffer, viewport, new Vector3(-reach, 0f, 0f), new Vector3(reach, 0f, 0f), AxisTint(UITheme.AxisX, Axis.X), AxisWidth(Axis.X), fadeStart, fadeEnd, AxisSegments);

                AppendWorldLine(_axisBuffer, viewport, new Vector3(0f, -reach, 0f), new Vector3(0f, reach, 0f), AxisTint(UITheme.AxisY, Axis.Y), AxisWidth(Axis.Y), fadeStart, fadeEnd, AxisSegments);

                AppendWorldLine(_axisBuffer, viewport, new Vector3(0f, 0f, -reach), new Vector3(0f, 0f, reach), AxisTint(UITheme.AxisZ, Axis.Z), AxisWidth(Axis.Z), fadeStart, fadeEnd, AxisSegments);

                _axisCount = _axisBuffer.Count;

                if (_axisArray.Length < _axisCount)
                    _axisArray = new VertexPositionColor[Math.Max(_axisCount, 256)];

                _axisBuffer.CopyTo(_axisArray);
            }

            if (_axisCount == 0)
                return;

            device.BlendState = _axisBlend;

            device.DepthStencilState = DepthStencilState.None;

            device.RasterizerState = _solidState;

            effect.CurrentTechnique = effect.Techniques["Axis"];

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();

                device.DrawUserPrimitives(PrimitiveType.TriangleList, _axisArray, 0, _axisCount / 3, VertexPositionColor.VertexDeclaration);
            }

            device.BlendState = BlendState.Opaque;

            device.DepthStencilState = DepthStencilState.Default;
        }

        private static float FadeAlpha(Vector3 world, float fadeStart, float fadeEnd)
        {
            var distance = world.Length();

            var t = Math.Clamp((distance - fadeStart) / MathF.Max(1f, fadeEnd - fadeStart), 0f, 1f);

            return 1f - (t * t * (3f - 2f * t));
        }

        private Axis? _axisKeyLock;

        private static bool IsAxisLocked(Axis axis) =>
            TransformOperator.Instance.Active && TransformOperator.Instance.Constraint.LockedAxis == axis;

        private const int HelperWidth = 1;

        private static readonly Color AxisLockBase = new(220, 220, 220);

        private static Color AxisTint(Color axisColor, Axis axis)
        {
            var baseColor = IsAxisLocked(axis) ? AxisLockBase : UITheme.GridLine;

            return Shade(Mix(baseColor, axisColor, 0.5f), -10);
        }

        private static Color Mix(Color a, Color b, float t) => new(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            a.A);

        private static Color Shade(Color color, int amount) => new(
            (byte)Math.Clamp(color.R + amount, 0, 255),
            (byte)Math.Clamp(color.G + amount, 0, 255),
            (byte)Math.Clamp(color.B + amount, 0, 255),
            color.A);

        private static int AxisWidth(Axis axis) => AxisThickness;

        private readonly List<VertexPositionColor> _lockBuffer = [];

        private VertexPositionColor[] _lockArray = new VertexPositionColor[256];

        private int _lockCount;

        private readonly List<VertexPositionColor> _helperBuffer = [];

        private VertexPositionColor[] _helperArray = new VertexPositionColor[256];

        private int _helperCount;

        private void DrawTransformHelper(GraphicsDevice device, Rectangle viewport, Effect effect)
        {
            var transform = TransformOperator.Instance;

            if (!transform.Active || transform.Targets == null || transform.Mode?.SpringHelpline != true)
            {
                _helperCount = 0;

                return;
            }

            BuildTransformHelper(viewport, transform);

            if (_helperCount == 0)
                return;

            device.BlendState = BlendState.AlphaBlend;

            device.DepthStencilState = DepthStencilState.None;

            device.RasterizerState = _solidState;

            effect.CurrentTechnique = effect.Techniques["Axis"];

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();

                device.DrawUserPrimitives(PrimitiveType.TriangleList, _helperArray, 0, _helperCount / 3, VertexPositionColor.VertexDeclaration);
            }

            device.BlendState = BlendState.Opaque;

            device.DepthStencilState = DepthStencilState.Default;
        }

        private void BuildTransformHelper(Rectangle viewport, TransformOperator transform)
        {
            _helperBuffer.Clear();

            if (!TryProject(transform.Targets!.Pivot, viewport, out var pivot))
                return;

            if (!TryProject(transform.Hit, viewport, out var mouse))
                return;

            var background = WithAlpha(UITheme.ViewportBackground, 128);

            var foreground = new Color((byte)255, (byte)255, (byte)255, (byte)255);

            AppendDashedPanelLine(viewport, pivot, mouse, background, HelperWidth + 1);

            AppendDashedPanelLine(viewport, pivot, mouse, foreground, HelperWidth);

            AppendPanelCross(viewport, pivot, foreground, HelperWidth);

            AppendSpringArrows(viewport, pivot, mouse, background, HelperWidth + 1);

            AppendSpringArrows(viewport, pivot, mouse, foreground, HelperWidth);

            _helperCount = _helperBuffer.Count;

            if (_helperArray.Length < _helperCount)
                _helperArray = new VertexPositionColor[Math.Max(_helperCount, 256)];

            _helperBuffer.CopyTo(_helperArray);
        }

        private bool TryProject(Vector3 world, Rectangle viewport, out PanelPoint point)
        {
            var view = Vector3.Transform(world, _viewMatrix);

            var clip = Vector4.Transform(new Vector4(view, 1f), _projectionMatrix);

            if (clip.W <= 0.0001f)
            {
                point = default;

                return false;
            }

            point = ToPanelPoint(clip, viewport, 1f);

            return true;
        }

        private void AppendPanelLine(Rectangle viewport, PanelPoint a, PanelPoint b, Color color, int thickness)
        {
            var dx = b.Position.X - a.Position.X;

            var dy = b.Position.Y - a.Position.Y;

            var length = MathF.Sqrt(dx * dx + dy * dy);

            if (length < 0.01f)
                return;

            var half = thickness / 2f;

            var nx = -dy / length * half;

            var ny = dx / length * half;

            var ca = WithAlpha(color, a.Alpha);

            var cb = WithAlpha(color, b.Alpha);

            _helperBuffer.Add(NdcVertex(a.Position.X + nx, a.Position.Y + ny, viewport, ca, a.Depth));

            _helperBuffer.Add(NdcVertex(b.Position.X + nx, b.Position.Y + ny, viewport, cb, b.Depth));

            _helperBuffer.Add(NdcVertex(b.Position.X - nx, b.Position.Y - ny, viewport, cb, b.Depth));

            _helperBuffer.Add(NdcVertex(a.Position.X + nx, a.Position.Y + ny, viewport, ca, a.Depth));

            _helperBuffer.Add(NdcVertex(b.Position.X - nx, b.Position.Y - ny, viewport, cb, b.Depth));

            _helperBuffer.Add(NdcVertex(a.Position.X - nx, a.Position.Y - ny, viewport, ca, a.Depth));
        }

        private void AppendDashedPanelLine(Rectangle viewport, PanelPoint a, PanelPoint b, Color color, int thickness)
        {
            var dx = b.Position.X - a.Position.X;

            var dy = b.Position.Y - a.Position.Y;

            var length = MathF.Sqrt(dx * dx + dy * dy);

            if (length < 0.5f)
                return;

            var period = 8f * UIScale.Factor;

            var steps = Math.Max(1, (int)(length / period));

            for (var i = 0; i < steps; i++)
            {
                var t0 = i / (float)steps;

                var t1 = (i + 0.5f) / steps;

                AppendPanelLine(viewport, InterpolatePoint(a, b, t0), InterpolatePoint(a, b, t1), color, thickness);
            }
        }

        private void AppendPanelCross(Rectangle viewport, PanelPoint point, Color color, int thickness)
        {
            var size = 6f * UIScale.Factor;

            AppendPanelLine(
                viewport,
                new PanelPoint { Position = point.Position + new Vector2(-size, 0f), Depth = point.Depth, Alpha = point.Alpha },
                new PanelPoint { Position = point.Position + new Vector2(size, 0f), Depth = point.Depth, Alpha = point.Alpha },
                color,
                thickness);

            AppendPanelLine(
                viewport,
                new PanelPoint { Position = point.Position + new Vector2(0f, -size), Depth = point.Depth, Alpha = point.Alpha },
                new PanelPoint { Position = point.Position + new Vector2(0f, size), Depth = point.Depth, Alpha = point.Alpha },
                color,
                thickness);
        }

        private void AppendSpringArrows(Rectangle viewport, PanelPoint pivot, PanelPoint mouse, Color color, int thickness)
        {
            var toPivot = pivot.Position - mouse.Position;

            if (toPivot.LengthSquared() < 1f)
                return;

            var length = toPivot.Length();

            var direction = toPivot / length;

            var perpendicular = new Vector2(direction.Y, -direction.X);

            var angle = TransformOperator.Instance.Mode?.ArrowAngle ?? 0f;

            if (angle != 0f)
            {
                var cos = MathF.Cos(angle);

                var sin = MathF.Sin(angle);

                var rotated = new Vector2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);

                perpendicular = new Vector2(rotated.Y, -rotated.X);

                direction = rotated;
            }

            AppendSpringArrow(viewport, mouse, direction, perpendicular, color, thickness, 1f);

            AppendSpringArrow(viewport, mouse, direction, perpendicular, color, thickness, -1f);
        }

        private void AppendSpringArrow(Rectangle viewport, PanelPoint mouse, Vector2 direction, Vector2 perpendicular, Color color, int thickness, float sign)
        {
            var offset = 5f * UIScale.Factor;

            var shaft = 10f * UIScale.Factor;

            var barb = 5f * UIScale.Factor;

            var adjust = 0.707f * UIScale.Factor;

            var start = mouse.Position + direction * (offset * sign);

            var end = start + direction * (shaft * sign);

            var shaftEnd = new PanelPoint { Position = end, Depth = mouse.Depth, Alpha = mouse.Alpha };

            AppendPanelLine(
                viewport,
                new PanelPoint { Position = start, Depth = mouse.Depth, Alpha = mouse.Alpha },
                shaftEnd,
                color,
                thickness);

            AppendPanelLine(
                viewport,
                shaftEnd,
                new PanelPoint { Position = end + direction * (adjust * sign) + perpendicular * adjust, Depth = mouse.Depth, Alpha = mouse.Alpha },
                color,
                thickness);

            AppendPanelLine(
                viewport,
                shaftEnd,
                new PanelPoint { Position = end - direction * (barb * sign) - perpendicular * barb, Depth = mouse.Depth, Alpha = mouse.Alpha },
                color,
                thickness);

            AppendPanelLine(
                viewport,
                shaftEnd,
                new PanelPoint { Position = end - direction * (barb * sign) + perpendicular * barb, Depth = mouse.Depth, Alpha = mouse.Alpha },
                color,
                thickness);
        }

        private void DrawLockedAxis(GraphicsDevice device, Rectangle viewport, Effect effect)
        {
            var transform = TransformOperator.Instance;

            var locked = transform.Active ? transform.Constraint.LockedAxis : null;

            if (locked == null)
            {
                _lockCount = 0;

                return;
            }

            GridMetrics(viewport, out _, out var reach, out var fadeStart, out var fadeEnd);

            _lockBuffer.Clear();

            var color = locked switch
            {
                Axis.X => UITheme.AxisX,

                Axis.Y => UITheme.AxisY,

                _ => UITheme.AxisZ
            };

            var from = locked switch
            {
                Axis.X => new Vector3(-reach, 0f, 0f),

                Axis.Y => new Vector3(0f, -reach, 0f),

                _ => new Vector3(0f, 0f, -reach)
            };

            var to = locked switch
            {
                Axis.X => new Vector3(reach, 0f, 0f),

                Axis.Y => new Vector3(0f, reach, 0f),

                _ => new Vector3(0f, 0f, reach)
            };

            AppendWorldLine(_lockBuffer, viewport, from, to, AxisTint(color, locked.Value), AxisWidth(locked.Value), fadeStart, fadeEnd, AxisSegments);

            _lockCount = _lockBuffer.Count;

            if (_lockArray.Length < _lockCount)
                _lockArray = new VertexPositionColor[Math.Max(_lockCount, 256)];

            _lockBuffer.CopyTo(_lockArray);

            device.BlendState = _axisBlend;

            device.DepthStencilState = DepthStencilState.None;

            device.RasterizerState = _solidState;

            effect.CurrentTechnique = effect.Techniques["Axis"];

            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();

                device.DrawUserPrimitives(PrimitiveType.TriangleList, _lockArray, 0, _lockCount / 3, VertexPositionColor.VertexDeclaration);
            }

            device.BlendState = BlendState.Opaque;

            device.DepthStencilState = DepthStencilState.Default;
        }

        private void AppendWorldLine(List<VertexPositionColor> vertices, Rectangle viewport, Vector3 from, Vector3 to, Color color, int thickness, float fadeStart, float fadeEnd, int segments)
        {
            var previous = from;

            var previousAlpha = FadeAlpha(from, fadeStart, fadeEnd);

            for (var i = 1; i <= segments; i++)
            {
                var next = Vector3.Lerp(from, to, i / (float)segments);

                var nextAlpha = i == segments ? FadeAlpha(to, fadeStart, fadeEnd) : FadeAlpha(next, fadeStart, fadeEnd);

                AppendWorldSegment(vertices, viewport, previous, next, color, thickness, previousAlpha, nextAlpha);

                previous = next;

                previousAlpha = nextAlpha;
            }
        }

        private void AppendWorldSegment(List<VertexPositionColor> vertices, Rectangle viewport, Vector3 from, Vector3 to, Color color, int thickness, float alphaFrom, float alphaTo)
        {
            var aView = Vector3.Transform(from, _viewMatrix);

            var bView = Vector3.Transform(to, _viewMatrix);

            const float NearLimit = -0.01f;

            if (aView.Z > NearLimit && bView.Z > NearLimit)
                return;

            if (aView.Z > NearLimit)
            {
                var t = (NearLimit - aView.Z) / (bView.Z - aView.Z);

                aView = Vector3.Lerp(aView, bView, t);

                alphaFrom = alphaFrom + (alphaTo - alphaFrom) * t;
            }
            else if (bView.Z > NearLimit)
            {
                var t = (NearLimit - bView.Z) / (aView.Z - bView.Z);

                bView = Vector3.Lerp(bView, aView, t);

                alphaTo = alphaTo + (alphaFrom - alphaTo) * t;
            }

            var aClip = Vector4.Transform(new Vector4(aView, 1f), _projectionMatrix);

            var bClip = Vector4.Transform(new Vector4(bView, 1f), _projectionMatrix);

            if (aClip.W <= 0.0001f || bClip.W <= 0.0001f)
                return;

            var a = ToPanelPoint(aClip, viewport, alphaFrom);

            var b = ToPanelPoint(bClip, viewport, alphaTo);

            if (!ClipToPanel(ref a, ref b, viewport))
                return;

            var dx = b.Position.X - a.Position.X;

            var dy = b.Position.Y - a.Position.Y;

            var length = MathF.Sqrt(dx * dx + dy * dy);

            if (length < 0.5f)
                return;

            var half = thickness / 2f;

            var nx = -dy / length * half;

            var ny = dx / length * half;

            var ca = WithAlpha(color, a.Alpha);

            var cb = WithAlpha(color, b.Alpha);

            vertices.Add(NdcVertex(a.Position.X + nx, a.Position.Y + ny, viewport, ca, a.Depth));
            vertices.Add(NdcVertex(b.Position.X + nx, b.Position.Y + ny, viewport, cb, b.Depth));
            vertices.Add(NdcVertex(b.Position.X - nx, b.Position.Y - ny, viewport, cb, b.Depth));

            vertices.Add(NdcVertex(a.Position.X + nx, a.Position.Y + ny, viewport, ca, a.Depth));
            vertices.Add(NdcVertex(b.Position.X - nx, b.Position.Y - ny, viewport, cb, b.Depth));
            vertices.Add(NdcVertex(a.Position.X - nx, a.Position.Y - ny, viewport, ca, a.Depth));
        }

        private static Color WithAlpha(Color color, float alpha) =>
            new(color.R, color.G, color.B, (byte)Math.Clamp(MathF.Round(255f * alpha), 0f, 255f));

        private static PanelPoint ToPanelPoint(Vector4 clip, Rectangle viewport, float alpha) =>
            new()
            {
                Position = new Vector2((clip.X / clip.W + 1f) * 0.5f * viewport.Width, (1f - clip.Y / clip.W) * 0.5f * viewport.Height),
                Depth = clip.Z / clip.W,
                Alpha = alpha
            };

        private static VertexPositionColor NdcVertex(float x, float y, Rectangle viewport, Color color, float depth) =>
            new(new Vector3(
                x / viewport.Width * 2f - 1f,
                1f - y / viewport.Height * 2f,
                depth), color);

        private static PanelPoint InterpolatePoint(PanelPoint a, PanelPoint b, float t) =>
            new()
            {
                Position = new Vector2(
                    a.Position.X + (b.Position.X - a.Position.X) * t,
                    a.Position.Y + (b.Position.Y - a.Position.Y) * t),
                Depth = a.Depth + (b.Depth - a.Depth) * t,
                Alpha = a.Alpha + (b.Alpha - a.Alpha) * t
            };

        private static bool ClipToPanel(ref PanelPoint a, ref PanelPoint b, Rectangle viewport)
        {
            var dx = b.Position.X - a.Position.X;

            var dy = b.Position.Y - a.Position.Y;

            var t0 = 0f;

            var t1 = 1f;

            if (!ClipParam(-dx, a.Position.X, ref t0, ref t1))
                return false;

            if (!ClipParam(dx, viewport.Width - a.Position.X, ref t0, ref t1))
                return false;

            if (!ClipParam(-dy, a.Position.Y, ref t0, ref t1))
                return false;

            if (!ClipParam(dy, viewport.Height - a.Position.Y, ref t0, ref t1))
                return false;

            var start = InterpolatePoint(a, b, t0);

            var end = InterpolatePoint(a, b, t1);

            a = start;

            b = end;

            return true;
        }

        private static bool ClipParam(float p, float q, ref float t0, ref float t1)
        {
            if (MathF.Abs(p) < 0.000001f)
                return q >= 0f;

            var r = q / p;

            if (p < 0f)
            {
                if (r > t1)
                    return false;

                if (r > t0)
                    t0 = r;

                return true;
            }

            if (r < t0)
                return false;

            if (r < t1)
                t1 = r;

            return true;
        }

        private void SetRoundedClip(Rectangle viewport, float radius)
        {
            if (radius <= 0f)
            {
                _effect.Parameters["xRoundParams"]?.SetValue(Vector4.Zero);

                return;
            }

            _effect.Parameters["xRoundSize"]?.SetValue(new Vector4(viewport.Width, viewport.Height, 0f, 0f));

            _effect.Parameters["xRoundParams"]?.SetValue(new Vector4(radius, 0f, 0f, 0f));
        }

        private RenderTarget2D _outlineTarget;

        private readonly List<VertexPositionColor> _gridBuffer = [];

        private VertexPositionColor[] _gridArray = [];

        private int _gridCount;

        private int _gridKeyWidth = -1;

        private int _gridKeyHeight = -1;

        private Matrix _gridKeyView;

        private readonly List<VertexPositionColor> _axisBuffer = [];

        private VertexPositionColor[] _axisArray = [];

        private int _axisCount;

        private int _axisKeyWidth = -1;

        private int _axisKeyHeight = -1;

        private Matrix _axisKeyView;

        private VertexPositionTexture[] _outlineQuad;

        private Vector3 _cameraPosition;

        private Vector3 _cameraTarget;

        private float _distance;

        private float _yaw;

        private float _pitch;

        private Vector3 _defaultTarget;

        private float _defaultDistance;

        private float _defaultYaw;

        private float _defaultPitch;

        private bool _selectAllRequested;

        private int _nameCounter;

        public string Title { get; private set; } = "NifRenderer";

        public GraphicsDevice GraphicsDevice => _device;

        public IReadOnlyList<RenderObject> Objects => _objects;

        public IReadOnlyCollection<RenderShape> Selection => _selection;

        public int SelectedShapeCount => _selection.Count;

        public int SelectedObjectCount
        {
            get
            {
                var count = 0;

                foreach (var item in _objects)
                {
                    if (ContainsObject(item))
                        count++;
                }

                return count;
            }
        }

        private bool ContainsObject(RenderObject item)
        {
            foreach (var shape in item.Mesh.RenderShapes)
            {
                if (_selection.Contains(shape))
                    return true;
            }

            return false;
        }

        public Rectangle ViewportRect
        {
            get => _viewportRect;

            set
            {
                if (_viewportRect == value)
                    return;

                _viewportRect = value;

                UpdateProjection();
            }
        }

        private Rectangle _viewportRect;

        private Matrix _viewProjectionInverse;

        private Matrix _viewProjectionKey;

        private Matrix ViewProjectionInverse
        {
            get
            {
                var key = _viewMatrix * _projectionMatrix;

                if (key != _viewProjectionKey)
                {
                    _viewProjectionKey = key;

                    _viewProjectionInverse = Matrix.Invert(key);
                }

                return _viewProjectionInverse;
            }
        }

        private const float RotationSpeed = 0.01f;

        private const float ZoomSpeed = 0.0015f;

        private const float ShiftPanMultiplier = 1.0f;

        private const float OutlineWidth = 3f;

        private readonly RenderTargetBinding[] _savedTargets = new RenderTargetBinding[1];

        private static readonly VertexPositionColor[] BackdropQuad =
        [
            new(new(-1f, 1f, 0f), UITheme.ViewportBackground),

            new(new(1f, 1f, 0f), UITheme.ViewportBackground),

            new(new(-1f, -1f, 0f), UITheme.ViewportBackground),

            new(new(1f, -1f, 0f), UITheme.ViewportBackground)
        ];

        private readonly MeshLoader _loader = new();

        public Renderer(GraphicsDevice device)
        {
            _effect = FxCompiler.Load("Content/Shaders/NifRender.fx", "Content/Shaders/NifRender.mgfx", device);

            _device = device ?? throw new ArgumentNullException(nameof(device));

            _solidState = new RasterizerState { CullMode = CullMode.None };

            Vertices = new VertexEditing(this);

            _outlineQuad =
            [
                new VertexPositionTexture(new Vector3(-1f, 1f, 0f), new Vector2(0f, 0f)),
                new VertexPositionTexture(new Vector3(1f, 1f, 0f), new Vector2(1f, 0f)),
                new VertexPositionTexture(new Vector3(-1f, -1f, 0f), new Vector2(0f, 1f)),
                new VertexPositionTexture(new Vector3(1f, -1f, 0f), new Vector2(1f, 1f))
            ];

            ResetCameraToDefault();

            _viewMatrix = CameraUtils.ViewMatrix(_yaw, _pitch, _cameraTarget, _distance);

            ViewportRect = _device.Viewport.Bounds;
        }

        public void RequestSelectAll() => _selectAllRequested = true;

        public void SelectAllIfRequested()
        {
            if (!_selectAllRequested)
                return;

            _selectAllRequested = false;

            SelectAll();
        }

        public bool IsLoading => _loader.Busy;

        public int PendingLoads => _loader.Pending;

        public string LoadingName => System.IO.Path.GetFileName(_loader.Current);

        public void Load(string path) => Load(path, new SceneTransform(Vector3.Zero, Vector3.Zero, 1f));

        public void Load(SceneNode node) => Load(node.ModelPath, node.Transform, node.IsVisible);

        public void Load(string path, SceneTransform transform, bool visible = true) =>
            _loader.Request(path, transform, visible);

        public void Update()
        {
            while (_loader.TryTake(out var loaded))
            {
                if (loaded.Error != null)
                {

                    continue;
                }

                try
                {
                    foreach (var pending in loaded.Textures)
                    {
                        pending.Shape.Texture?.Dispose();

                        pending.Shape.Texture = pending.Pixels == null
                            ? Texture2DUtils.FromPath(_device, pending.Path)
                            : Texture2DUtils.Upload(_device, pending.Width, pending.Height, pending.Pixels);

                        pending.Shape.ApplyUVs();
                    }
                }
                catch (Exception exception)
                {
                }

                loaded.Mesh.TexturedCount = loaded.Textures.Count;

                var item = new RenderObject(loaded.Mesh, loaded.Path, $"obj{++_nameCounter}", loaded.Transform);
                item.SetVisible(loaded.Visible);
                _objects.Add(item);

                TryLoadSliders(loaded.Mesh, loaded.Path);

                UpdateSceneBounds();

                UpdateTitle();
            }
        }

        private static void TryLoadSliders(RenderMesh mesh, string nifPath)
        {
            if (string.IsNullOrEmpty(nifPath))
                return;

            var osdPath = System.IO.Path.ChangeExtension(nifPath, ".osd");

            if (!System.IO.File.Exists(osdPath))
                return;

            var name = System.IO.Path.GetFileNameWithoutExtension(osdPath);

            var ospPath = FindSliderSet(System.IO.Path.GetDirectoryName(osdPath), name);

            if (ospPath == null)
            {

                return;
            }

            try
            {
                mesh.Slides = SliderSet.Load(ospPath);

                mesh.Applicator = new SliderApplicator(mesh.Slides);

            }
            catch (Exception exception)
            {
                mesh.Slides = null;

                mesh.Applicator = null;

            }
        }

        private static string? FindSliderSet(string? folder, string name)
        {
            while (!string.IsNullOrEmpty(folder))
            {
                var candidate = System.IO.Path.Combine(folder, "SliderSets", name + ".osp");

                if (System.IO.File.Exists(candidate))
                    return candidate;

                var parent = System.IO.Path.GetDirectoryName(folder);

                if (parent == folder)
                    break;

                folder = parent;
            }

            return null;
        }

        public void AddTextureSource(string directory) => TexturePathResolver.Sources.Add(directory);

        private void ResetCameraToDefault()
        {
            const float cameraHeight = 20f;

            _cameraPosition = new Vector3(0f, 100f, cameraHeight);

            _cameraTarget = new Vector3(0f, 0f, cameraHeight);

            _distance = (_cameraPosition - _cameraTarget).Length();

            _yaw = MathF.Atan2(_cameraPosition.Y - _cameraTarget.Y, _cameraPosition.X - _cameraTarget.X);

            _pitch = MathF.Asin(Math.Clamp((_cameraPosition.Z - _cameraTarget.Z) / _distance, -1f, 1f));

            _defaultTarget = _cameraTarget;

            _defaultDistance = _distance;

            _defaultYaw = _yaw;

            _defaultPitch = _pitch;
        }

        private void ResetCamera()
        {
            _cameraTarget = _defaultTarget;

            _distance = _defaultDistance;

            _yaw = _defaultYaw;

            _pitch = _defaultPitch;

            _cameraPosition = _cameraTarget + CameraUtils.DirectionOf(_yaw, _pitch) * _distance;

            _viewMatrix = CameraUtils.ViewMatrix(_yaw, _pitch, _cameraTarget, _distance);
        }

        public void Rotate(int dx, int dy)
        {
            _yaw += dx * RotationSpeed;

            _pitch += dy * RotationSpeed;

            UpdateView();
        }

        public void Pan(int dx, int dy)
        {
            var viewDir = Vector3.Normalize(_cameraTarget - _cameraPosition);

            var right = Vector3.Normalize(Vector3.Cross(viewDir, Vector3.UnitZ));

            var up = Vector3.Normalize(Vector3.Cross(right, viewDir));

            var halfHeight = MathF.Tan(MathHelper.PiOver4 * 0.5f) * _distance;

            var viewportHeight = _viewportRect.Height > 0 ? _viewportRect.Height : _device.Viewport.Height;

            var worldPerPixel = 2f * halfHeight / MathF.Max(1f, viewportHeight);

            _cameraTarget += (-dx * right + dy * up) * worldPerPixel * ShiftPanMultiplier;

            UpdateView();
        }

        public void Zoom(int wheelDelta)
        {
            _distance *= 1.0f - wheelDelta * ZoomSpeed;

            var radius = _sceneRadius > 0f ? _sceneRadius : _defaultDistance;

            _distance = Math.Clamp(_distance, radius * 0.15f, radius * 8f);

            UpdateView();
        }

        private float _sceneRadius;

        private void UpdateSceneBounds()
        {
            var min = new Vector3(float.MaxValue);

            var max = new Vector3(float.MinValue);

            var found = false;

            foreach (var item in _objects)
            {
                if (item.Mesh.BoundMin == item.Mesh.BoundMax)
                    continue;

                min = Vector3.Min(min, item.Mesh.BoundMin);

                max = Vector3.Max(max, item.Mesh.BoundMax);

                found = true;
            }

            _sceneRadius = found ? (max - min).Length() * 0.5f : 0f;
        }

        public void ToggleWireframe()
        {
            ShowWireframe = !ShowWireframe;
        }

        public void ResetView()
        {
            ResetCamera();
        }

        public void SelectAt(int x, int y, bool shift, bool control)
        {
            var hit = Pick(x, y);

            ClickShape(hit, shift, control);

            UpdateTitle();
        }

        public void EnsureVisible()
        {
            ToggleSelectionVisibility();
        }

        public void Frame()
        {
            FrameSelected();
        }

        public RenderObject? SingleSelectedObject()
        {
            RenderObject? found = null;

            foreach (var item in _objects)
            {
                if (!AnyShapeSelected(item))
                    continue;

                if (found != null)
                    return null;

                found = item;
            }

            return found;
        }

        public string DefaultExportPath()
        {
            var item = SingleSelectedObject();

            return item == null ? "" : NifExporter.DefaultPath(item);
        }

        public RenderMesh? ActiveMesh
        {
            get
            {
                foreach (var item in _objects)
                {
                    if (AnyShapeSelected(item))
                        return item.Mesh;
                }

                return _objects.Count > 0 ? _objects[0].Mesh : null;
            }
        }

        private RenderMesh? SelectedMesh()
        {
            foreach (var item in _objects)
            {
                if (AnyShapeSelected(item))
                    return item.Mesh;
            }

            return null;
        }

        private static float SegmentDistance(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;

            var lengthSquared = ab.LengthSquared();

            if (lengthSquared < 1e-6f)
                return (point - a).Length();

            var t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0f, 1f);

            return (point - (a + ab * t)).Length();
        }

        private static bool Inside(Vector2 a, Vector2 b, Vector2 c, float x, float y)
        {
            var d1 = (x - b.X) * (a.Y - b.Y) - (a.X - b.X) * (y - b.Y);

            var d2 = (x - c.X) * (b.Y - c.Y) - (b.X - c.X) * (y - c.Y);

            var d3 = (x - a.X) * (c.Y - a.Y) - (c.X - a.X) * (y - a.Y);

            var negative = d1 < 0f || d2 < 0f || d3 < 0f;

            var positive = d1 > 0f || d2 > 0f || d3 > 0f;

            return !(negative && positive);
        }

        public bool TryProjectScreen(Vector3 world, Rectangle viewport, out Vector2 screen, out float depth)
        {
            screen = default;

            depth = 0f;

            if (!TryProject(world, viewport, out var x, out var y, out depth))
                return false;

            screen = new Vector2(x + viewport.X, y + viewport.Y);

            return true;
        }

        public RenderObject? ActiveObject()
        {
            var mesh = ActiveMesh;

            if (mesh == null)
                return null;

            foreach (var item in _objects)
            {
                if (ReferenceEquals(item.Mesh, mesh))
                    return item;
            }

            return null;
        }

        public string ExportObject(RenderObject item, string target)
        {
            LastExportMessage = NifExporter.Export(item, target);


            return LastExportMessage;
        }

        public string ExportCurrent(string target)
        {
            if (_objects.Count == 0)
                return "没有已加载的模型";

            var item = SingleSelectedObject();

            if (item == null)
                return "请先在列表里选中一个模型";

            return ExportObject(item, target);
        }

        public string LastExportMessage = "";

        public string ProjectPath = "";

        public void ClearSelection()
        {
            if (SelectionLocked || _selection.Count == 0)
                return;

            _selection.Clear();

            UpdateTitle();
        }

        public void SelectAllObjects()
        {
            if (SelectionLocked)
                return;

            SelectAll();
        }

        private void UpdateView()
        {
            _cameraPosition = _cameraTarget + CameraUtils.DirectionOf(_yaw, _pitch) * _distance;

            _viewMatrix = CameraUtils.ViewMatrix(_yaw, _pitch, _cameraTarget, _distance);
        }

        private float SceneExtent() => GridSpacingValue * GridMaxCells;

        private void UpdateProjection()
        {
            var aspect = _viewportRect.Height > 0
                ? (float)_viewportRect.Width / _viewportRect.Height
                : _device.Viewport.AspectRatio;

            var far = MathF.Max(1000f, (_distance + SceneExtent()) * 1.3f);

            _projectionMatrix = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, aspect, 1f, far);
        }

        public void DeleteSelected()
        {
            if (SelectionLocked)
                return;

            var shapes = Selected();

            if (shapes.Count == 0)
                return;

            var record = new DeleteShapesRecord(this, shapes, _selection);

            record.Redo();

            _history.Record(record);

            RefreshAfterEdit();
        }

        public History History => _history;

        private readonly History _history = new();

        private void RefreshAfterEdit()
        {
            _selection.RemoveWhere(shape => !_objects.Exists(item => item.Mesh.RenderShapes.Contains(shape)));

            Vertices.PruneTargets();

            Vertices.PruneSelection();

            Vertices.MarkDotsDirty();

            UpdateSceneBounds();

            UpdateTitle();
        }

        public bool Undo()
        {
            if (_history.Undo() == null)
                return false;

            RefreshAfterEdit();

            return true;
        }

        public void SelectInBox(Rectangle box, bool shift, bool control)
        {
            if (SelectionLocked)
                return;

            var viewport = _viewportRect;

            if (viewport.Width <= 0 || viewport.Height <= 0)
                return;

            if (!shift && !control)
                _selection.Clear();

            foreach (var item in _objects)
            {
                _effect.Parameters["xWorld"]?.SetValue(item.WorldMatrix);

                foreach (var shape in item.Mesh.RenderShapes)
                {
                    if (!shape.Visible)
                        continue;

                    if (ShapeInBox(shape, box, viewport))
                        _selection.Add(shape);
                }
            }

            UpdateTitle();
        }

        private bool ShapeInBox(RenderShape shape, Rectangle box, Rectangle viewport)
        {
            var vertices = shape.Mesh.Vertices;

            for (var i = 0; i < shape.VertexCount; i++)
            {
                if (shape.IsVertexDeleted(i))
                    continue;

                var index = shape.StartIndex + i;

                if (index < 0 || index >= vertices.Length)
                    continue;

                if (!TryProject(vertices[index].Position, viewport, out var x, out var y))
                    continue;

                if (box.Contains((int)x, (int)y))
                    return true;
            }

            return false;
        }

        public VertexEditing Vertices { get; }

        private const float WireframeWidth = 2f;

        [System.Runtime.InteropServices.DllImport("opengl32.dll", EntryPoint = "glLineWidth")]
        private static extern void GlLineWidthNative(float width);

        private static void GlLineWidth(float width)
        {
            try
            {
                GlLineWidthNative(width);
            }
            catch (DllNotFoundException)
            {
            }

            catch (EntryPointNotFoundException)
            {
            }
        }

        public Rectangle Viewport => _viewportRect;

        public RasterizerState SolidState => _solidState;

        public EditMode VertexMode => Vertices.Mode;

        public bool IsEditMode => Vertices.Active;

        public EditorMode Mode { get; private set; } = EditorMode.Object;

        public void SetMode(EditorMode mode)
        {
            if (Mode == mode)
                return;

            var wantEdit = mode == EditorMode.Edit;

            if (wantEdit != Vertices.Active)
            {
                Vertices.Toggle();

                if (wantEdit && !Vertices.Active)
                    return;
            }

            Mode = mode;
        }

        public void ToggleEditMode() => SetMode(IsEditMode ? EditorMode.Object : EditorMode.Edit);

        public bool TryProject(Vector3 world, Rectangle viewport, out float x, out float y)
        {
            return TryProject(world, viewport, out x, out y, out _);
        }

        public bool TryProject(Vector3 world, Rectangle viewport, out float x, out float y, out float depth)
        {
            x = 0f;

            y = 0f;

            depth = 0f;

            if (viewport.Width <= 0 || viewport.Height <= 0)
                return false;

            var clip = Vector4.Transform(new Vector4(world, 1f), _viewMatrix * _projectionMatrix);

            if (clip.W <= 0.0001f)
                return false;

            var ndcX = clip.X / clip.W;

            var ndcY = clip.Y / clip.W;

            if (ndcX < -1.05f || ndcX > 1.05f || ndcY < -1.05f || ndcY > 1.05f)
                return false;

            x = viewport.X + (ndcX * 0.5f + 0.5f) * viewport.Width;

            y = viewport.Y + (0.5f - ndcY * 0.5f) * viewport.Height;

            depth = clip.Z / clip.W;

            return true;
        }

        public bool Redo()
        {
            if (_history.Redo() == null)
                return false;

            RefreshAfterEdit();

            return true;
        }

        internal List<RenderObject> ObjectList => _objects;

        internal void RemoveObjectAt(int index)
        {
            if (index < 0 || index >= _objects.Count)
                return;

            var mesh = _objects[index].Mesh;

            mesh.Dispose();

            _objects.RemoveAt(index);
        }

        internal void InsertObjectAt(int index, RenderObject item) => _objects.Insert(Math.Clamp(index, 0, _objects.Count), item);

        internal void AddToSelection(RenderShape shape) => _selection.Add(shape);

        internal void RemoveFromSelection(RenderShape shape) => _selection.Remove(shape);

        private void SelectAll()
        {
            _selection.Clear();

            foreach (var item in _objects)
            {
                foreach (var shape in item.Mesh.RenderShapes)
                    _selection.Add(shape);
            }

            UpdateTitle();
        }

        private void ForgetRemoved()
        {
            _selection.RemoveWhere(shape => !_objects.Exists(item => ReferenceEquals(item.Mesh, shape.Mesh)));
        }

        private List<RenderObject> SelectedObjects()
        {
            var result = new List<RenderObject>();

            foreach (var item in _objects)
            {
                if (ContainsObject(item))
                    result.Add(item);
            }

            return result;
        }

        private List<RenderShape> Selected()
        {
            var result = new List<RenderShape>(_selection.Count);

            foreach (var item in _objects)
            {
                foreach (var shape in item.Mesh.RenderShapes)
                {
                    if (_selection.Contains(shape))
                        result.Add(shape);
                }
            }

            return result;
        }

        public List<RenderShape> SelectedShapes() => Selected();

        public bool SelectionLocked => false;

        public bool IsSelected(RenderShape shape) => _selection.Contains(shape);

        public bool IsSelected(RenderObject item) => AllShapesSelected(item);

        public bool IsPartiallySelected(RenderObject item)
        {
            var selected = 0;

            var total = 0;

            foreach (var shape in item.Mesh.RenderShapes)
            {
                total++;

                if (_selection.Contains(shape))
                    selected++;
            }

            return selected > 0 && selected < total;
        }

        private bool AllShapesSelected(RenderObject item)
        {
            var shapes = item.Mesh.RenderShapes;

            if (shapes.Count == 0)
                return false;

            foreach (var shape in shapes)
            {
                if (!_selection.Contains(shape))
                    return false;
            }

            return true;
        }

        public void ClickShape(RenderShape? shape, bool shift, bool control)
        {
            if (SelectionLocked)
                return;

            if (shape == null)
            {
                if (!shift && !control)
                    _selection.Clear();

                return;
            }

            if (control)
            {
                if (!_selection.Add(shape))
                    _selection.Remove(shape);

                return;
            }

            if (shift && _selection.Count > 0)
            {
                var ordered = AllShapes();

                var anchor = ordered.FindIndex(selected => _selection.Contains(selected));

                var target = ordered.IndexOf(shape);

                if (anchor >= 0 && target >= 0)
                {
                    var start = Math.Min(anchor, target);

                    var end = Math.Max(anchor, target);

                    for (var i = start; i <= end; i++)
                        _selection.Add(ordered[i]);

                    return;
                }
            }

            if (_selection.Count == 1 && _selection.Contains(shape))
                return;

            _selection.Clear();

            _selection.Add(shape);
        }

        public void ClickObject(RenderObject? item, bool shift, bool control)
        {
            if (item == null)
            {
                ClickShape(null, shift, control);

                return;
            }

            if (control)
            {
                var all = true;

                foreach (var shape in item.Mesh.RenderShapes)
                    all &= _selection.Contains(shape);

                foreach (var shape in item.Mesh.RenderShapes)
                {
                    if (all)
                        _selection.Remove(shape);
                    else
                        _selection.Add(shape);
                }

                return;
            }

            if (shift && _selection.Count > 0)
            {
                var anchor = _objects.FindIndex(ContainsObject);

                var target = _objects.IndexOf(item);

                if (anchor >= 0 && target >= 0)
                {
                    var start = Math.Min(anchor, target);

                    var end = Math.Max(anchor, target);

                    for (var i = start; i <= end && i < _objects.Count; i++)
                    {
                        foreach (var shape in _objects[i].Mesh.RenderShapes)
                            _selection.Add(shape);
                    }

                    return;
                }
            }

            _selection.Clear();

            foreach (var shape in item.Mesh.RenderShapes)
                _selection.Add(shape);
        }

        private List<RenderShape> AllShapes()
        {
            var result = new List<RenderShape>();

            foreach (var item in _objects)
            {
                foreach (var shape in item.Mesh.RenderShapes)
                    result.Add(shape);
            }

            return result;
        }

        public void ToggleVisibility(RenderObject item)
        {
            if (item == null)
                return;

            var shapes = new List<RenderShape>();

            foreach (var shape in item.Mesh.RenderShapes)
                shapes.Add(shape);

            ApplyVisibility(shapes, !item.AnyVisible);
        }

        public void ToggleVisibility(RenderShape shape)
        {
            if (shape == null)
                return;

            ApplyVisibility([shape], !shape.Visible);
        }

        public void ToggleSelectionVisibility()
        {
            var shapes = Selected();

            if (shapes.Count == 0)
                return;

            ApplyVisibility(shapes, shapes.Exists(shape => !shape.Visible));
        }

        private void ApplyVisibility(IReadOnlyList<RenderShape> shapes, bool visible)
        {
            if (shapes.Count == 0)
                return;

            var record = new VisibilityRecord(shapes, visible);

            record.Redo();

            _history.Record(record);

            RefreshAfterEdit();
        }

        public bool AnyShapeSelected(RenderObject item) => IsSelected(item) || IsPartiallySelected(item);

        private void FrameSelected()
        {
            var shapes = Selected();

            var hasBounds = false;

            var min = Vector3.Zero;

            var max = Vector3.Zero;

            if (shapes.Count == 0)
            {
                foreach (var item in _objects)
                {
                    var meshMin = item.Mesh.BoundMin;

                    var meshMax = item.Mesh.BoundMax;

                    if (meshMin == meshMax)
                        continue;

                    if (!hasBounds)
                    {
                        min = meshMin;

                        max = meshMax;

                        hasBounds = true;

                        continue;
                    }

                    min = Vector3.Min(min, meshMin);

                    max = Vector3.Max(max, meshMax);
                }
            }
            else
            {
                foreach (var shape in shapes)
                {
                    foreach (var vertex in shape.ShapeVertices)
                    {
                        if (!hasBounds)
                        {
                            min = vertex.Position;

                            max = vertex.Position;

                            hasBounds = true;

                            continue;
                        }

                        min = Vector3.Min(min, vertex.Position);

                        max = Vector3.Max(max, vertex.Position);
                    }
                }
            }

            if (!hasBounds)
                return;

            var center = (min + max) * 0.5f;

            var radius = MathF.Max((max - center).Length(), 1e-3f);

            var fov = MathHelper.PiOver4;

            _cameraTarget = center;

            _distance = radius / MathF.Sin(fov * 0.5f) * 1.15f;

            _yaw = MathHelper.ToRadians(35f);

            _pitch = MathHelper.ToRadians(18f);

            _cameraPosition = _cameraTarget + CameraUtils.DirectionOf(_yaw, _pitch) * _distance;

            _viewMatrix = CameraUtils.ViewMatrix(_yaw, _pitch, _cameraTarget, _distance);
        }

        public Vector2? Project(Vector3 world)
        {
            var viewport = _viewportRect;

            if (viewport.Width <= 0 || viewport.Height <= 0)
                return null;

            var clip = Vector4.Transform(new Vector4(world, 1f), _viewMatrix * _projectionMatrix);

            if (clip.W <= 1e-6f)
                return null;

            var x = clip.X / clip.W;

            var y = clip.Y / clip.W;

            return new Vector2(
                viewport.X + (x * 0.5f + 0.5f) * viewport.Width,
                viewport.Y + (0.5f - y * 0.5f) * viewport.Height);
        }

        public Vector3 VertexPosition(VertexRef vertex)
        {
            var mesh = vertex.Shape.Mesh;

            var index = vertex.Shape.StartIndex + vertex.Index;

            if (index < 0 || index >= mesh.Vertices.Length)
                return Vector3.Zero;

            return mesh.Vertices[index].Position;
        }

        public static bool RayPlane(Vector3 origin, Vector3 direction, Plane plane, out Vector3 hit)
        {
            hit = Vector3.Zero;

            var denominator = Vector3.Dot(plane.Normal, direction);

            if (MathF.Abs(denominator) < 1e-6f)
                return false;

            var distance = (-plane.D - Vector3.Dot(plane.Normal, origin)) / denominator;

            if (distance < 0f)
                return false;

            hit = origin + direction * distance;

            return true;
        }

        public bool ScreenRay(int screenX, int screenY, out Vector3 origin, out Vector3 direction)
        {
            origin = Vector3.Zero;

            direction = Vector3.Zero;

            var viewport = _viewportRect;

            if (viewport.Width <= 0 || viewport.Height <= 0)
                return false;

            var localX = screenX - viewport.X;

            var localY = screenY - viewport.Y;

            var inverse = ViewProjectionInverse;

            var near = Unproject(inverse, viewport, localX, localY, 0f);

            var far = Unproject(inverse, viewport, localX, localY, 1f);

            var delta = far - near;

            if (delta.LengthSquared() < 1e-12f)
                return false;

            origin = near;

            direction = Vector3.Normalize(delta);

            return true;
        }

        public Vector3 ViewDirection => CameraUtils.DirectionOf(_yaw, _pitch);

        public RenderShape? Pick(int screenX, int screenY)
        {
            var viewport = _viewportRect;

            if (viewport.Width <= 0 || viewport.Height <= 0)
                return null;

            if (!viewport.Contains(screenX, screenY))
                return null;

            var localX = screenX - viewport.X;

            var localY = screenY - viewport.Y;

            var inverse = ViewProjectionInverse;

            var near = Unproject(inverse, viewport, localX, localY, 0f);

            var far = Unproject(inverse, viewport, localX, localY, 1f);

            var direction = far - near;

            if (direction.LengthSquared() < 1e-12f)
                return null;

            direction.Normalize();

            RenderShape? best = null;

            var bestDistance = float.MaxValue;

            foreach (var item in _objects)
            {
                if (!item.AnyVisible)
                    continue;

                var shape = item.RayPick(near, direction, out var distance);

                if (shape != null && distance >= 0f && distance < bestDistance)
                {
                    bestDistance = distance;

                    best = shape;
                }
            }

            return best;
        }

        private static Vector3 Unproject(Matrix inverse, Rectangle viewport, float x, float y, float depth)
        {
            var ndcX = x / viewport.Width * 2f - 1f;

            var ndcY = 1f - y / viewport.Height * 2f;

            var ndcZ = depth * 2f - 1f;

            var world = Vector4.Transform(new Vector4(ndcX, ndcY, ndcZ, 1f), inverse);

            return new Vector3(world.X, world.Y, world.Z) / world.W;
        }

        private void UpdateTitle()
        {
            var selected = _selection.Count;

            var suffix = selected > 0 ? $" [{selected} of {_objects.Count} selected]" : $" [{_objects.Count} objects]";

            Title = $"NifRenderer{suffix}";
        }

        public void Draw(GraphicsDevice device)
        {
            UpdateProjection();

            device.BlendState = BlendState.Opaque;

            device.DepthStencilState = DepthStencilState.Default;

            device.RasterizerState = _solidState;

            _effect.Parameters["xView"].SetValue(_viewMatrix);

            _effect.Parameters["xProjection"].SetValue(_projectionMatrix);

            _effect.Parameters["xWorld"].SetValue(Matrix.Identity);

            if (_selection.Count > 0 && !Vertices.Active)
                RenderSelectionMask(device);

            var viewport = _viewportRect;

            if (viewport.Width <= 0 || viewport.Height <= 0)
                viewport = device.Viewport.Bounds;

            device.Viewport = new Viewport(viewport);

            device.ScissorRectangle = viewport;

            device.Clear(ClearOptions.DepthBuffer | ClearOptions.Stencil, UITheme.ViewportBackground, 1f, 0);

            device.DepthStencilState = DepthStencilState.None;

            device.BlendState = BlendState.Opaque;

            SetRoundedClip(viewport, UIMetrics.CornerRadius);

            _effect.CurrentTechnique = _effect.Techniques["Pretransformed"];

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();

                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, BackdropQuad, 0, 2, VertexPositionColor.VertexDeclaration);
            }

            device.BlendState = BlendState.Opaque;

            device.DepthStencilState = DepthStencilState.Default;

            device.RasterizerState = _solidState;

            var lightDirection = Vector3.Normalize(new Vector3(0f, 1f, 1f));

            _effect.Parameters["xLightDirection"].SetValue(lightDirection);

            _effect.Parameters["xAmbient"].SetValue(0.4f);

            _effect.Parameters["xEnableLighting"].SetValue(true);

            DrawGrid(device, viewport, _effect);

            DrawAxes(device, viewport, _effect);


            _effect.CurrentTechnique = _effect.Techniques["ColoredNoShading"];

            foreach (var item in _objects)
                item.Draw(device, _effect);

            device.BlendState = BlendState.AlphaBlend;

            device.DepthStencilState = DepthStencilState.DepthRead;

            foreach (var item in _objects)
                item.DrawBlended(device, _effect);

            device.BlendState = BlendState.Opaque;

            device.DepthStencilState = DepthStencilState.Default;

            if (ShowWireframe && !Vertices.Active)
            {
                foreach (var item in _objects)
                    item.DrawWireframe(device, _effect, Color.Red);
            }

            if (Vertices.Active)
            {
                GlLineWidth(WireframeWidth);

                foreach (var item in _objects)
                    item.DrawWireframe(device, _effect, Vertices.LineColor, Vertices.ContainsTarget);

                GlLineWidth(1f);

                Vertices.Draw(device, viewport, _effect);

                DrawLockedAxis(device, viewport, _effect);

                DrawTransformHelper(device, viewport, _effect);

                return;
            }

            if (_selection.Count > 0)
                DrawSelectionOutline(device);

            DrawLockedAxis(device, viewport, _effect);

            DrawTransformHelper(device, viewport, _effect);
        }

        private void RenderSelectionMask(GraphicsDevice device)
        {
            EnsureOutlineTarget(device);

            if (_outlineTarget == null)
                return;

            device.GetRenderTargets(_savedTargets);

            var outerTarget = _savedTargets.Length > 0 ? _savedTargets[0].RenderTarget as RenderTarget2D : null;

            var outerViewport = device.Viewport;

            device.SetRenderTarget(_outlineTarget);

            try
            {
                device.Viewport = new Viewport(0, 0, _outlineTarget.Width, _outlineTarget.Height);

                device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1f, 0);

                device.BlendState = BlendState.Opaque;

                device.DepthStencilState = DepthStencilState.Default;

                device.RasterizerState = _solidState;

                _effect.CurrentTechnique = _effect.Techniques["OutlineID"];

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();

                    foreach (var item in _objects)
                    {
                        foreach (var shape in item.Mesh.RenderShapes)
                        {
                            if (shape.Visible && _selection.Contains(shape))
                                shape.Draw(device);
                        }
                    }
                }
            }
            finally
            {
                device.SetRenderTarget(outerTarget);

                device.Viewport = outerViewport;
            }
        }

        public void Dispose()
        {
            Vertices.ReleaseBuffer();

            _outlineTarget?.Dispose();

            _outlineTarget = null;

            _gridArray = [];

            _axisArray = [];

            foreach (var item in _objects)
                item.Mesh.Dispose();

            _effect?.Dispose();

            _solidState?.Dispose();

            _axisBlend?.Dispose();
        }

        private void EnsureOutlineTarget(GraphicsDevice device)
        {

            var viewport = _viewportRect;

            if (viewport.Width <= 0 || viewport.Height <= 0)
                viewport = device.Viewport.Bounds;

            var width = Math.Max(1, viewport.Width);

            var height = Math.Max(1, viewport.Height);

            if (_outlineTarget != null && _outlineTarget.Width == width && _outlineTarget.Height == height)
                return;

            _outlineTarget?.Dispose();

            _outlineTarget = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24);
        }

        private static readonly Vector4 SelectionOutlineColor = new(1f, 0.55f, 0.1f, 1f);

        private static readonly Vector4 TransformOutlineColor = new(1f, 1f, 1f, 1f);

        private void DrawSelectionOutline(GraphicsDevice device)
        {
            EnsureOutlineTarget(device);

            if (_outlineTarget == null)
                return;

            SetOutlineParameter("xTexture", _outlineTarget);

            SetOutlineParameter("xOutlineTexel", new Vector2(1f / _outlineTarget.Width, 1f / _outlineTarget.Height));

            SetOutlineParameter("xOutlineWidth", OutlineWidth);

            var outline = TransformOperator.Instance.Active ? TransformOutlineColor : SelectionOutlineColor;

            SetOutlineParameter("xOutlineColor", outline);

            device.BlendState = BlendState.NonPremultiplied;

            device.DepthStencilState = DepthStencilState.None;

            _effect.CurrentTechnique = _effect.Techniques["OutlineDetect"];

            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply();

                device.DrawUserPrimitives(PrimitiveType.TriangleStrip, _outlineQuad, 0, 2, VertexPositionTexture.VertexDeclaration);
            }

            device.DepthStencilState = DepthStencilState.Default;

            device.BlendState = BlendState.Opaque;
        }

        private void SetOutlineParameter(string name, object value)
        {
            var parameter = _effect.Parameters[name];

            if (parameter == null)
                return;

            switch (value)
            {
                case Texture2D texture:
                    parameter.SetValue(texture);

                    break;

                case Vector4 vector4:
                    parameter.SetValue(vector4);

                    break;

                case Vector2 vector2:
                    parameter.SetValue(vector2);

                    break;

                case float single:
                    parameter.SetValue(single);

                    break;
            }
        }
    }
}
