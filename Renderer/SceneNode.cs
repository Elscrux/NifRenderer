using System;
namespace NifRenderer.Renderer;

public readonly record struct SceneNode(string ModelPath, Transform Transform, bool IsVisible = true) {
	public Guid Id { get; init; } = Guid.NewGuid();
}
