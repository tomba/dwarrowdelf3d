﻿#define USE_NONPARALLEL

using Dwarrowdelf;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client3D
{
	class ChunkManager : Component
	{
		Chunk[] m_chunks;

		TerrainRenderer m_scene;

		public int VerticesRendered { get; private set; }
		public int ChunksRendered { get; private set; }
		public int ChunkRecalcs { get; private set; }

		public ChunkManager(TerrainRenderer scene)
		{
			m_scene = scene;

			CreateChunks();
		}

		void CreateChunks()
		{
			var map = m_scene.Map;

			int xChunks = map.Size.Width / Chunk.CHUNK_SIZE;
			int yChunks = map.Size.Height / Chunk.CHUNK_SIZE;
			int zChunks = map.Size.Depth / Chunk.CHUNK_SIZE;

			m_chunks = new Chunk[xChunks * yChunks * zChunks];

			// Organize chunks from up to down to avoid overdraw
			int idx = 0;
			for (int z = zChunks - 1; z >= 0; --z)
			{
				for (int y = 0; y < yChunks; ++y)
				{
					for (int x = 0; x < xChunks; ++x)
					{
						var chunkOffset = new IntVector3(x, y, z) * Chunk.CHUNK_SIZE;
						var chunk = ToDispose(new Chunk(map, chunkOffset));
						m_chunks[idx++] = chunk;
					}
				}
			}
		}

		public void InvalidateChunks()
		{
			foreach (var chunk in m_chunks)
				chunk.InvalidateChunk();
		}

		public void InvalidateChunksZ(int fromZ, int toZ)
		{
			foreach (var chunk in m_chunks)
			{
				if (chunk.ChunkOffset.Z <= toZ && chunk.ChunkOffset.Z + Chunk.CHUNK_SIZE >= fromZ)
					chunk.InvalidateChunk();
			}
		}

		public void Update(GameTime gameTime)
		{
			var cameraService = m_scene.Services.GetService<ICameraService>();

			var frustum = cameraService.Frustum;

			int numVertices = 0;
			int numChunks = 0;
			int numChunkRecalcs = 0;

#if USE_NONPARALLEL
			foreach (var chunk in m_chunks)
#else
			Parallel.ForEach(m_chunks, chunk =>
#endif
			{
				var res = frustum.Contains(ref chunk.BBox);

				if (res == ContainmentType.Disjoint)
				{
					chunk.IsEnabled = false;

					chunk.Free();
				}
				else
				{
					chunk.IsEnabled = true;

					if (chunk.IsInvalid)
						Interlocked.Increment(ref numChunkRecalcs);

					chunk.Update(m_scene);

					Interlocked.Add(ref numVertices, chunk.VertexCount);
					Interlocked.Increment(ref numChunks);
				}
#if USE_NONPARALLEL
			}
#else
			});
#endif
			this.VerticesRendered = numVertices;
			this.ChunksRendered = numChunks;
			this.ChunkRecalcs = numChunkRecalcs;
		}

		public void Draw(GameTime gameTime)
		{
			var cameraService = m_scene.Services.GetService<ICameraService>();

			// Vertex Shader

			var viewProjMatrix = Matrix.Transpose(cameraService.View * cameraService.Projection);
			viewProjMatrix.Transpose();
			m_scene.Effect.Parameters["g_viewProjMatrix"].SetValue(ref viewProjMatrix);

			// Pixel Shader
			m_scene.Effect.Parameters["g_eyePos"].SetValue(cameraService.Position);

			var perObCBuf = m_scene.Effect.ConstantBuffers["PerObjectBuffer"];

			foreach (var chunk in m_chunks)
			{
				if (chunk.IsEnabled == false)
					continue;

				var worldMatrix = Matrix.Translation(chunk.ChunkOffset.ToVector3());
				perObCBuf.Parameters["worldMatrix"].SetValue(ref worldMatrix);
				perObCBuf.Update();

				chunk.Render(m_scene);
			}
		}
	}
}
