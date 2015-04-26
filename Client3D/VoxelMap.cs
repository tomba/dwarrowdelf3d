﻿using Dwarrowdelf;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Client3D
{
	class VoxelMap
	{
		public event Action<IntVector3> VoxelChanged;

		public Voxel[, ,] Grid { get; private set; }
		public IntSize3 Size { get; private set; }

		public int Width { get { return this.Size.Width; } }
		public int Height { get { return this.Size.Height; } }
		public int Depth { get { return this.Size.Depth; } }

		public VoxelMap(IntSize3 size)
		{
			//System.Diagnostics.Trace.Assert(Marshal.SizeOf<Voxel>() == 4);

			this.Size = size;
			this.Grid = new Voxel[size.Depth, size.Height, size.Width];
		}

		public VoxelMap(IntSize3 size, Voxel init)
			: this(size)
		{
			Clear(init);
		}

		public void Clear(Voxel init)
		{
			Parallel.For(0, this.Depth, z =>
			{
				for (int y = 0; y < this.Height; ++y)
					for (int x = 0; x < this.Width; ++x)
					{
						this.Grid[z, y, x] = init;
					}
			});
		}

		public void SetVoxel(IntVector3 p, Voxel voxel)
		{
			this.Grid[p.Z, p.Y, p.X] = voxel;

			this.Grid[p.Z, p.Y, p.X].VisibleFaces = GetVisibleFaces(p);

			if (this.VoxelChanged != null)
				this.VoxelChanged(p);

			// TODO: optimize, we only need to check the faces towards the voxel that is set

			foreach (var v in IntVector3.CardinalUpDownDirections)
			{
				var n = p + v;

				if (!this.Size.Contains(n))
					continue;

				if (this.Grid[n.Z, n.Y, n.X].Type == VoxelType.Empty)
					continue;

				this.Grid[n.Z, n.Y, n.X].VisibleFaces = GetVisibleFaces(n);

				if (this.VoxelChanged != null)
					this.VoxelChanged(n);
			}
		}

		public Voxel GetVoxel(IntVector3 p)
		{
			return this.Grid[p.Z, p.Y, p.X];
		}

		public void CheckVisibleFaces(bool undefineHidden)
		{
			var grid = this.Grid;

			Parallel.For(0, this.Depth, z =>
			{
				for (int y = 0; y < this.Height; ++y)
					for (int x = 0; x < this.Width; ++x)
					{
						var p = new IntVector3(x, y, z);

						this.Grid[p.Z, p.Y, p.X].VisibleFaces = GetVisibleFaces(p);
					}
			});
		}

		Direction GetVisibleFaces(IntVector3 p)
		{
			Direction visibleFaces = 0;

			foreach (var dir in DirectionExtensions.CardinalUpDownDirections)
			{
				var n = p + dir;

				if (this.Size.Contains(n) == false)
					continue;

				if (this.Grid[n.Z, n.Y, n.X].IsOpaque)
					continue;

				// slope above hides the top face, but not if the slope is at side or below
				if (dir == Direction.Up && this.Grid[n.Z, n.Y, n.X].Type == VoxelType.Slope)
					continue;

				visibleFaces |= dir;
			}

			return visibleFaces;
		}

		public void FillFromNoiseMap(SharpNoise.NoiseMap map)
		{
			var max = map.Data.Max();
			var min = map.Data.Min();

			Parallel.For(0, map.Data.Length, i =>
			{
				var v = map.Data[i];	// [-1 .. 1]

				v -= min;
				v /= (max - min);		// [0 .. 1]

				v *= this.Depth * 8 / 10;
				v += this.Depth * 2 / 10;

				map.Data[i] = v;
			});

			var grid = this.Grid;

			int waterLimit = this.Depth * 3 / 10;
			int grassLimit = this.Depth * 8 / 10;

			Parallel.For(0, this.Height, y =>
			{
				for (int x = 0; x < this.Width; ++x)
				{
					var v = map[x, y];

					int iv = (int)v;

					for (int z = this.Depth - 1; z >= 0; --z)
					{
						/* above ground */
						if (z > iv)
						{
							//if (z < waterLimit)
							//	grid[z, y, x] = Voxel.Water;
							//else
							grid[z, y, x] = Voxel.Empty;
						}
						/* surface */
						else if (z == iv)
						{
							bool isSlope;

							isSlope = CheckForSlope(new IntVector3(x, y, z), ref grid[z, y, x], iv, map);

							if (!isSlope)
								grid[z, y, x] = Voxel.Rock;

							if (z >= waterLimit && z < grassLimit)
							{
								grid[z, y, x].Flags = VoxelFlags.Grass;

								Dwarrowdelf.MWCRandom r = new MWCRandom(new IntVector3(x, y, z), 0);
								if (r.Next(100) < 30)
								{
									grid[z + 1, y, x].Flags |= VoxelFlags.Tree;
									//grid[z, y, x].Flags |= VoxelFlags.Tree2;
								}
							}
						}
						/* underground */
						else if (z < iv)
						{
							grid[z, y, x] = Voxel.Rock;
						}
						else
						{
							throw new Exception();
						}
					}
				}
			});
		}

		public static VoxelMap CreateBallMap(int side, int innerSide = 0)
		{
			var map = new VoxelMap(new IntSize3(side, side, side));

			var grid = map.Grid;

			int r = side / 2 - 1;
			int ir = innerSide / 2 - 1;

			Parallel.For(0, side, z =>
			{
				for (int y = 0; y < side; ++y)
					for (int x = 0; x < side; ++x)
					{
						var pr = Math.Sqrt((x - r) * (x - r) + (y - r) * (y - r) + (z - r) * (z - r));

						if (pr < r && pr >= ir)
							grid[z, y, x].Type = VoxelType.Rock;
						else
							grid[z, y, x].Type = VoxelType.Empty;
					}
			});

			return map;
		}

		public static VoxelMap CreateCubeMap(int side, int offset)
		{
			var map = new VoxelMap(new IntSize3(side, side, side));

			var grid = map.Grid;

			map.Clear(Voxel.Empty);

			for (int z = offset; z < side - offset; ++z)
				for (int y = offset; y < side - offset; ++y)
					for (int x = offset; x < side - offset; ++x)
					{
						grid[z, y, x].Type = VoxelType.Rock;
					}

			return map;
		}

		public unsafe static VoxelMap Load(string path)
		{
			using (var stream = File.OpenRead(path))
			{
				VoxelMap map;

				using (var br = new BinaryReader(stream, Encoding.Default, true))
				{
					int w = br.ReadInt32();
					int h = br.ReadInt32();
					int d = br.ReadInt32();

					var size = new IntSize3(w, h, d);

					map = new VoxelMap(size);
				}

				fixed (Voxel* v = map.Grid)
				{
					byte* p = (byte*)v;
					using (var memStream = new UnmanagedMemoryStream(p, 0, map.Size.Volume * sizeof(Voxel), FileAccess.Write))
						stream.CopyTo(memStream);
				}

				return map;
			}
		}

		public unsafe void Save(string path)
		{
			using (var stream = File.Create(path))
			{
				using (var bw = new BinaryWriter(stream, Encoding.Default, true))
				{
					bw.Write(this.Size.Width);
					bw.Write(this.Size.Height);
					bw.Write(this.Size.Depth);
				}

				fixed (Voxel* v = this.Grid)
				{
					byte* p = (byte*)v;
					using (var memStream = new UnmanagedMemoryStream(p, this.Size.Volume * sizeof(Voxel)))
						memStream.CopyTo(stream);
				}
			}
		}

		class SlopeInfo
		{
			public byte SlopeType;
			public byte Mask;
		}

		static SlopeInfo[] s_slopeInfos = new SlopeInfo[] {
			// .XX
			// . X
			// ...
			new SlopeInfo() { SlopeType = 4, Mask = (1 << 3) - 1, },
			// XXX
			// . X
			// ...
			new SlopeInfo() { SlopeType = 4, Mask = RotRight8((1 << 4) - 1, 1), },
			// .XX
			// . X
			// ..X
			new SlopeInfo() { SlopeType = 4, Mask = (1 << 4) - 1, },
			// XXX
			// . X
			// ..X
			new SlopeInfo() { SlopeType = 4, Mask = RotRight8((1 << 5) - 1, 1), },

			// .X.
			// . .
			// ...
			new SlopeInfo() { SlopeType = 0, Mask = RotRight8((1 << 1) - 1, 0), },
			// .XX
			// . .
			// ...
			new SlopeInfo() { SlopeType = 0, Mask = RotRight8((1 << 2) - 1, 0), },
			// XX.
			// . .
			// ...
			new SlopeInfo() { SlopeType = 0, Mask = RotRight8((1 << 2) - 1, 1), },
			// XXX
			// . .
			// ...
			new SlopeInfo() { SlopeType = 0, Mask = RotRight8((1 << 3) - 1, 1), },
			// XXX
			// X X
			// ...
			new SlopeInfo() { SlopeType = 0, Mask = RotRight8((1 << 5) - 1, 2), },
			// XXX
			// X X
			// ..X
			new SlopeInfo() { SlopeType = 0, Mask = RotRight8((1 << 6) - 1, 2), },
			// XXX
			// X X
			// X..
			new SlopeInfo() { SlopeType = 0, Mask = RotRight8((1 << 6) - 1, 3), },
			// XXX
			// X X
			// X.X
			new SlopeInfo() { SlopeType = 0, Mask = RotRight8((1 << 7) - 1, 3), },

			// XXX
			// X X
			// .XX
			new SlopeInfo() { SlopeType = 2, Mask = RotRight8((1 << 7) - 1, 2), },
		};

		static IntVector2[] s_surroundVectors = new IntVector2[8] {
			new IntVector2(0, -1),
			new IntVector2(1, -1),
			new IntVector2(1, 0),
			new IntVector2(1, 1),
			new IntVector2(0, 1),
			new IntVector2(-1, 1),
			new IntVector2(-1, 0),
			new IntVector2(-1, -1),
		};

		static byte RotLeft8(byte value, int count)
		{
			return (byte)((value << count) | (value >> (8 - count)));
		}

		static byte RotRight8(byte value, int count)
		{
			return (byte)((value >> count) | (value << (8 - count)));
		}

		bool CheckForSlope(IntVector3 p, ref Voxel voxel, int iv, SharpNoise.NoiseMap map)
		{
			int highDirs = 0;

			for (int i = 0; i < 8; ++i)
			{
				var np = p + s_surroundVectors[i];

				if (!this.Size.Contains(np) || (int)map[np.X, np.Y] >= iv)
					highDirs |= 1 << i;
			}

			if (highDirs == 0 || highDirs == 0xff)
				return false;

			for (int i = 0; i < 4; ++i)
			{
				byte dir;
				switch (i)
				{
					case 0: dir = 2; break;
					case 1: dir = 1; break;
					case 2: dir = 3; break;
					case 3: dir = 0; break;
					default:
						throw new Exception();
				}

				foreach (var si in s_slopeInfos)
				{
					if (si.Mask == highDirs)
					{
						voxel = new Voxel()
						{
							Type = VoxelType.Slope,
							Dir = dir,
							SlopeType = si.SlopeType,
						};

						return true;
					}
				}

				highDirs = RotRight8((byte)highDirs, 2);
			}

			return false;
		}
	}
}
