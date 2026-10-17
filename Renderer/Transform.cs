using Microsoft.Xna.Framework;
namespace NifRenderer.Renderer;

public readonly record struct Transform(Vector3 Position, Vector3 Rotation, float Scale)
{
	public Matrix ToMatrix()
	{
		var radians = MathHelper.ToRadians;
		var rotation = Matrix.CreateRotationX(radians(Rotation.X))
			* Matrix.CreateRotationY(radians(Rotation.Y))
			* Matrix.CreateRotationZ(radians(Rotation.Z));

		return Matrix.CreateScale(Scale) * rotation * Matrix.CreateTranslation(Position);
	}
}
