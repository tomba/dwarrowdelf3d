using SharpDX;
using SharpDX.Toolkit;
using SharpDX.Toolkit.Graphics;
using System.Threading.Tasks;

namespace Client3D
{
	sealed class MarchingCubesRenderer : GameSystem
	{
		ICameraService m_cameraService;

		GeometricPrimitive m_cube;
		Texture2D m_cubeTexture;
		Matrix m_cubeTransform;

		BasicEffect m_basicEffect;

		public MarchingCubesRenderer(Game game)
			: base(game)
		{
			this.Visible = true;
			this.Enabled = true;

			game.GameSystems.Add(this);
		}

		public override void Initialize()
		{
			base.Initialize();

			m_cameraService = Services.GetService<ICameraService>();
		}

		protected override void LoadContent()
		{
			base.LoadContent();

			m_basicEffect = ToDisposeContent(new BasicEffect(GraphicsDevice));

			m_basicEffect.EnableDefaultLighting();
			m_basicEffect.TextureEnabled = true;

			m_cubeTexture = Content.Load<Texture2D>("logo_large");

			GenArray();
			var sw = System.Diagnostics.Stopwatch.StartNew();
			GenMesh();
			sw.Stop();
			System.Windows.Forms.MessageBox.Show(string.Format("Took {0} ms", sw.ElapsedMilliseconds));
		}

		const int SIZE = 256;

		void GenArray()
		{
			var noise = new SharpNoise.Modules.Simplex()
			{
				OctaveCount = 1,
			};

			int size = SIZE;
			var arr = new float[size, size, size];
			Parallel.For(0, size, x =>
			//for (int x = 0; x < size; x++)
			{
				for (int y = 0; y < size; y++)
				{
					for (int z = 0; z < size; z++)
					{
						var v = new Vector3(x, y, z);
						v /= size;
						v -= 0.5f;
						v *= 2;
						arr[x, y, z] = (float)noise.GetValue(v.X, v.Y, v.Z);
						//arr[x, y, z] = v.X * v.X + v.Y * v.Y + v.Z * v.Z - 1;
					}
				}
			});
			m_arr = arr;
		}

		void GenMesh()
		{
			Poligonizator.Init(SIZE - 1, m_arr, this.GraphicsDevice);
			m_mesh = Poligonizator.Process(this.GraphicsDevice, 0.0f);
			m_prim = new GeometricPrimitive(this.GraphicsDevice, m_mesh.m_vertices.ToArray(), m_mesh.m_indices.ToArray(),
				true);
		}

		float[, ,] m_arr;
		Client3D.Poligonizator.MarchCubesPrimitive m_mesh;
		GeometricPrimitive m_prim;

		public override void Update(GameTime gameTime)
		{
			base.Update(gameTime);

			var time = (float)gameTime.TotalGameTime.TotalSeconds;

			//m_cubeTransform = Matrix.RotationX(time) * Matrix.RotationY(time * 2f) * Matrix.RotationZ(time * .7f);
			m_cubeTransform = Matrix.Identity;

			m_basicEffect.View = m_cameraService.View;
			m_basicEffect.Projection = m_cameraService.Projection;
		}

		public override void Draw(GameTime gameTime)
		{
			base.Draw(gameTime);

			this.GraphicsDevice.SetRasterizerState(this.GraphicsDevice.RasterizerStates.CullNone);

			m_basicEffect.Texture = m_cubeTexture;
			m_basicEffect.World = m_cubeTransform;

			if (m_prim != null)
				m_prim.Draw(m_basicEffect);
		}
	}
}
