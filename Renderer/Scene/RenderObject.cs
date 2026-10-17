using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using NifRenderer.Renderer;
using System;

namespace NifRenderer.Renderer.Scene
{
    public class RenderObject
    {
        public RenderMesh Mesh { get; }

        public string Path { get; }

        public string FileName { get; }

        public string Name { get; set; }

        public Transform Transform { get; set; }

        public Matrix WorldMatrix => Transform.ToMatrix();

        public bool AllVisible
        {
            get
            {
                foreach (var shape in Mesh.RenderShapes)
                {
                    if (!shape.Visible)
                        return false;
                }

                return Mesh.RenderShapes.Count > 0;
            }
        }

        public bool AnyVisible
        {
            get
            {
                foreach (var shape in Mesh.RenderShapes)
                {
                    if (shape.Visible)
                        return true;
                }

                return false;
            }
        }

        public void SetVisible(bool visible)
        {
            foreach (var shape in Mesh.RenderShapes)
                shape.Visible = visible;
        }

        public RenderObject(RenderMesh mesh, string path, string name, Transform transform)
        {
            Mesh = mesh;

            Path = path;

            FileName = string.IsNullOrEmpty(path) ? "unnamed" : System.IO.Path.GetFileName(path);

            Name = string.IsNullOrEmpty(name) ? FileName : name;

            Transform = transform;
        }

        public void Draw(GraphicsDevice device, Effect effect)
        {
            effect.Parameters["xWorld"]?.SetValue(WorldMatrix);
            Mesh.Draw(device, effect);
        }

        public void DrawBlended(GraphicsDevice device, Effect effect)
        {
            effect.Parameters["xWorld"]?.SetValue(WorldMatrix);
            Mesh.DrawBlended(device, effect);
        }

        public void DrawWireframe(GraphicsDevice device, Effect effect, Color color, Predicate<RenderShape>? filter = null)
        {
            effect.Parameters["xWorld"]?.SetValue(WorldMatrix);
            Mesh.DrawWireframe(device, effect, color, filter);
        }

        public void DrawWireframe(GraphicsDevice device, Effect effect, Func<RenderShape, int, Color> colorOf, Predicate<RenderShape>? filter = null)
        {
            effect.Parameters["xWorld"]?.SetValue(WorldMatrix);
            Mesh.DrawWireframe(device, effect, colorOf, filter);
        }

        public RenderShape? RayPick(Vector3 origin, Vector3 direction, out float distance)
        {
            var inverse = Matrix.Invert(WorldMatrix);
            var localOrigin = Vector3.Transform(origin, inverse);
            var localDirection = Vector3.TransformNormal(direction, inverse);

            if (localDirection.LengthSquared() < 1e-12f)
            {
                distance = -1f;
                return null;
            }

            localDirection.Normalize();
            var shape = Mesh.RayPick(localOrigin, localDirection, out distance);
            if (shape == null)
            {
                distance = -1f;
                return null;
            }

            var localHit = localOrigin + localDirection * distance;
            distance = Vector3.Distance(origin, Vector3.Transform(localHit, WorldMatrix));
            return shape;
        }

        public override string ToString() => Name;
    }
}
