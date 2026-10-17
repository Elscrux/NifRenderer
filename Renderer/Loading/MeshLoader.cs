using NifRenderer.Resolver;
using NifRenderer.Utils;
using NiflySharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NifRenderer.Renderer.Loading
{
    public sealed class PendingTexture(RenderShape shape, string path, int width, int height, byte[] pixels)
    {
        public readonly RenderShape Shape = shape;

        public readonly string Path = path;

        public readonly int Width = width;

        public readonly int Height = height;

        public readonly byte[] Pixels = pixels;
    }

    public sealed class LoadedMesh(RenderMesh mesh, string path, Transform transform, bool visible)
    {
        public readonly RenderMesh Mesh = mesh;

        public readonly string Path = path;

        public readonly Transform Transform = transform;

        public readonly bool Visible = visible;

        public readonly List<PendingTexture> Textures = [];

        public Exception Error;
    }

    public sealed class MeshLoader
    {
        private readonly ConcurrentQueue<(string Path, Transform Transform, bool Visible)> _requests = new();

        private readonly ConcurrentQueue<LoadedMesh> _completed = new();

        private readonly Lock _gate = new();

        private bool _running;

        public string Current { get; private set; } = "";

        public bool Busy
        {
            get
            {
                lock (_gate)
                    return _running || !_requests.IsEmpty || !_completed.IsEmpty;
            }
        }

        public int Pending
        {
            get
            {
                lock (_gate)
                    return (_running ? 1 : 0) + _requests.Count + _completed.Count;
            }
        }

        public void Request(string path, Transform transform, bool visible = true)
        {
            if (!Path.GetExtension(path).Equals(".nif", StringComparison.CurrentCultureIgnoreCase))
                return;

            _requests.Enqueue((path, transform, visible));

            Pump();
        }

        public bool TryTake(out LoadedMesh loaded) => _completed.TryDequeue(out loaded);

        public void Pump()
        {
            (string Path, Transform Transform, bool Visible) request;

            lock (_gate)
            {
                if (_running || !_requests.TryDequeue(out request))
                    return;

                _running = true;

                Current = request.Path;
            }

            Task.Run(() => Run(request.Path, request.Transform, request.Visible));
        }

        private void Run(string path, Transform transform, bool visible)
        {
            try
            {
                var file = new NifFile();

                file.Load(path);

                var mesh = new RenderMesh(file, path);

                var loaded = new LoadedMesh(mesh, path, transform, visible);

                DecodeTextures(loaded);

                _completed.Enqueue(loaded);
            }
            catch (Exception exception)
            {
                _completed.Enqueue(new LoadedMesh(null, path, transform, visible) { Error = exception });
            }
            finally
            {
                lock (_gate)
                    _running = false;

                Pump();
            }
        }

        private static void DecodeTextures(LoadedMesh loaded)
        {
            foreach (var shape in loaded.Mesh.RenderShapes)
            {
                var texturePath = Texture2DUtils.GetTexturePath(loaded.Mesh.File, shape.Shape);

                if (texturePath == null)
                    continue;

                var resolved = TexturePathResolver.Resolve(loaded.Path, texturePath);

                if (resolved == null)
                {
                    loaded.Mesh.MissingTextureCount++;


                    continue;
                }

                try
                {
                    byte[] pixels = null;

                    var width = 0;

                    var height = 0;

                    if (Texture2DUtils.IsDds(resolved))
                        pixels = Texture2DUtils.DecodeDds(resolved, out width, out height);

                    loaded.Textures.Add(new(shape, resolved, width, height, pixels));

                }
                catch (Exception exception)
                {
                    loaded.Mesh.MissingTextureCount++;

                }
            }
        }
    }
}
