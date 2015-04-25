﻿using Dwarrowdelf;
using SharpDX;
using SharpDX.Toolkit;
using SharpDX.Toolkit.Graphics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Client3D
{
	class SymbolRenderer : GameSystem
	{
		SymbolEffect m_effect;

		int m_vertexCount;
		Buffer<SceneryVertex> m_vertexBuffer;

		MovableManager m_manager;

		bool m_invalid;

		public SymbolRenderer(Game game, MovableManager manager)
			: base(game)
		{
			this.Visible = true;
			this.Enabled = true;

			m_invalid = true;

			m_manager = manager;
			MovableObject.MovableMoved += MovableObject_MovableMoved;

			game.GameSystems.Add(this);
		}

		void MovableObject_MovableMoved(MovableObject obj)
		{
			m_invalid = true;
		}

		public override void Initialize()
		{
			base.Initialize();

			this.Services.GetService<ViewGridProvider>().ViewGridCornerChanged +=
				(oldValue, newValue) => m_invalid = true;
		}

		protected override void LoadContent()
		{
			base.LoadContent();

			m_effect = this.Content.Load<SymbolEffect>("SymbolEffect");

			m_effect.SymbolTextures = this.Content.Load<Texture2D>("TileSetTextureArray");
		}

		Color ToColor(GameColor color)
		{
			var rgb = color.ToGameColorRGB();
			return new Color(rgb.R, rgb.G, rgb.B);
		}

		void UpdateVertexBuffer()
		{
			IntGrid3 viewGrid = this.Services.GetService<ViewGridProvider>().ViewGrid;

			var vertices = new VertexList<SceneryVertex>(m_manager.Movables.Count);

			foreach (var m in m_manager.Movables)
			{
				if (viewGrid.Contains(m.Position) == false)
					continue;

				vertices.Add(new SceneryVertex(m.Position.ToVector3(), ToColor(m.Color), (uint)m.SymbolID));
			}

			if (vertices.Count > 0)
			{
				if (m_vertexBuffer == null || m_vertexBuffer.ElementCount < vertices.Count)
				{
					RemoveAndDispose(ref m_vertexBuffer);
					m_vertexBuffer = ToDispose(Buffer.Vertex.New<SceneryVertex>(this.GraphicsDevice, vertices.Count));
				}

				m_vertexBuffer.SetData(vertices.Data, 0, vertices.Count);
			}

			m_vertexCount = vertices.Count;
		}

		public override void Draw(GameTime gameTime)
		{
			if (m_invalid)
			{
				UpdateVertexBuffer();
				m_invalid = false;
			}

			if (m_vertexCount == 0)
				return;

			var device = this.GraphicsDevice;

			var camera = this.Services.GetService<CameraProvider>();

			m_effect.EyePos = camera.Position;
			m_effect.ViewProjection = camera.View * camera.Projection;

			var angle = (float)System.Math.Acos(Vector3.Dot(-Vector3.UnitZ, camera.Look));
			angle = MathUtil.RadiansToDegrees(angle);
			if (System.Math.Abs(angle) < 45)
				m_effect.CurrentTechnique = m_effect.Techniques["ModeFlat"];
			else
				m_effect.CurrentTechnique = m_effect.Techniques["ModeFollow"];

			m_effect.CurrentTechnique.Passes[0].Apply();

			var offset = new IntVector3();
			m_effect.SetPerObjectConstBuf(offset);

			device.SetBlendState(device.BlendStates.AlphaBlend);
			//device.SetDepthStencilState(device.DepthStencilStates.None);

			device.SetVertexBuffer(m_vertexBuffer);
			device.Draw(PrimitiveType.PointList, m_vertexCount);

			device.SetBlendState(device.BlendStates.Default);
			//device.SetDepthStencilState(device.DepthStencilStates.Default);
		}
	}
}
