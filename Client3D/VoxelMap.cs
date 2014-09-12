using Dwarrowdelf;
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
	enum VoxelType : byte
	{
		Undefined = 0,
		Empty,
		Rock,
		Water,
		Slope,
	}

	enum VoxelFlags : byte
	{
		None = 0,
		Grass = 1 << 0,
		Tree = 1 << 1,
		Tree2 = 1 << 2,
	}

	[StructLayout(LayoutKind.Explicit, Size = 8)]
	struct Voxel
	{
		[FieldOffset(0)]
		public ulong Raw;

		[FieldOffset(0)]
		public VoxelType Type;
		[FieldOffset(1)]
		public FaceDirectionBits VisibleFaces;
		[FieldOffset(2)]
		public VoxelFlags Flags;
		[FieldOffset(3)]
		public byte Dir;
		[FieldOffset(4)]
		public byte SlopeType;

		public bool IsUndefined { get { return this.Type == VoxelType.Undefined; } }
		public bool IsEmpty { get { return this.Type == VoxelType.Empty; } }

		public readonly static Voxel Undefined = new Voxel() { Type = VoxelType.Undefined };
		public readonly static Voxel Empty = new Voxel() { Type = VoxelType.Empty };
		public readonly static Voxel Rock = new Voxel() { Type = VoxelType.Rock };
		public readonly static Voxel Water = new Voxel() { Type = VoxelType.Water };
		public readonly static Voxel Slope = new Voxel() { Type = VoxelType.Slope };
	}

	class VoxelMap
	{
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

		public Voxel GetVoxel(IntVector3 p)
		{
			return this.Grid[p.Z, p.Y, p.X];
		}

		public void CheckSlopeDirs()
		{
			var grid = this.Grid;

			Parallel.For(0, this.Depth, z =>
			{
				for (int y = 0; y < this.Height; ++y)
					for (int x = 0; x < this.Width; ++x)
					{
						if (grid[z, y, x].Type != VoxelType.Slope)
							continue;

						var p = new IntVector3(x, y, z);

						CheckSlopeForTile(p, ref grid[z, y, x]);
					}
			});
		}

		void CheckSlopeForTile(IntVector3 p, ref Voxel vox)
		{
			var plane = this.Size.Plane;

			DirectionSet dirSet = new DirectionSet();

			var ivec = Direction.East.ToIntVector2();
			for (int i = 0; i < 8; ++i)
			{
				var np = p + ivec;

				if (plane.Contains(np.ToIntVector2()) && this.Grid[np.Z, np.Y, np.X].Type == VoxelType.Rock)
					dirSet |= ivec.ToDirection().ToDirectionSet();

				ivec = ivec.FastRotate(1);
			}

			if (dirSet == DirectionSet.None)
				return;
			//throw new Exception();

			/* in problematic cases we just pick one slope direction */

			switch (dirSet & DirectionSet.Cardinal)
			{
				case DirectionSet.East:
				case DirectionSet.North | DirectionSet.East | DirectionSet.South:
					vox.SlopeType = 0;
					vox.Dir = 1;
					return;

				case DirectionSet.South:
				case DirectionSet.East | DirectionSet.South | DirectionSet.West:
					vox.SlopeType = 0;
					vox.Dir = 3;
					return;

				case DirectionSet.West:
				case DirectionSet.West | DirectionSet.East:
				case DirectionSet.South | DirectionSet.West | DirectionSet.North:
					vox.SlopeType = 0;
					vox.Dir = 0;
					return;

				case DirectionSet.North:
				case DirectionSet.North | DirectionSet.South:
				case DirectionSet.West | DirectionSet.North | DirectionSet.East:
				case DirectionSet.West | DirectionSet.North | DirectionSet.East | DirectionSet.South:
					vox.SlopeType = 0;
					vox.Dir = 2;
					return;

				case DirectionSet.East | DirectionSet.North:
					vox.SlopeType = 1;
					vox.Dir = 2;
					return;

				case DirectionSet.East | DirectionSet.South:
					vox.SlopeType = 1;
					vox.Dir = 1;
					return;

				case DirectionSet.West | DirectionSet.South:
					vox.SlopeType = 1;
					vox.Dir = 3;
					return;

				case DirectionSet.West | DirectionSet.North:
					vox.SlopeType = 1;
					vox.Dir = 0;
					return;

				default:
					break;
			}

			if ((dirSet & DirectionSet.NorthEast) != 0)
			{
				vox.SlopeType = 3;
				vox.Dir = 2;
				return;
			}

			if ((dirSet & DirectionSet.SouthEast) != 0)
			{
				vox.SlopeType = 3;
				vox.Dir = 1;
				return;
			}

			if ((dirSet & DirectionSet.SouthWest) != 0)
			{
				vox.SlopeType = 3;
				vox.Dir = 3;
				return;
			}

			if ((dirSet & DirectionSet.NorthWest) != 0)
			{
				vox.SlopeType = 3;
				vox.Dir = 0;
				return;
			}
		}


		public void CheckVisibleFaces()
		{
			var grid = this.Grid;

			Parallel.For(0, this.Depth, z =>
			{
				for (int y = 0; y < this.Height; ++y)
					for (int x = 0; x < this.Width; ++x)
					{
						var p = new IntVector3(x, y, z);

						var neighbors = DirectionSet.CardinalUpDown.ToSurroundingPoints(p);

						foreach (var n in neighbors)
						{
							var dir = (n - p).ToDirection();

							if (this.Size.Contains(n) == true)
							{
								if (grid[n.Z, n.Y, n.X].Type == VoxelType.Rock || grid[n.Z, n.Y, n.X].Type == VoxelType.Undefined)
									continue;

								// slope above hides the top face, but not if the slope is at side or below
								if (dir == Direction.Up && grid[n.Z, n.Y, n.X].Type == VoxelType.Slope)
									continue;
							}

							grid[z, y, x].VisibleFaces |= dir.ToFaceDirectionBits();
						}
					}
			});
		}

		public void UndefineHiddenVoxels()
		{
			var size = this.Size;

			var visibilityArray = new bool[size.Depth, size.Height, size.Width];

			for (int z = size.Depth - 1; z >= 0; --z)
			{
				bool lvlIsHidden = true;

				Parallel.For(0, size.Height, y =>
				{
					for (int x = 0; x < size.Width; ++x)
					{
						bool visible;

						// Air tiles are always visible
						if (this.Grid[z, y, x].IsEmpty)
						{
							visible = true;
						}
						else
						{
							var p = new IntVector3(x, y, z);
							visible = DirectionSet.All.ToSurroundingPoints(p)
								.Where(this.Size.Contains)
								.Any(n => GetVoxel(n).IsEmpty);
						}

						if (visible)
						{
							lvlIsHidden = false;
							visibilityArray[z, y, x] = true;
						}
					}
				});

				// if the whole level is not visible, the levels below cannot be seen either
				if (lvlIsHidden)
					break;
			}

			for (int z = this.Depth - 1; z >= 0; --z)
			{
				Parallel.For(0, this.Height, y =>
				{
					for (int x = 0; x < this.Width; ++x)
					{
						if (visibilityArray[z, y, x] == false)
						{
							this.Grid[z, y, x] = Voxel.Undefined;
						}
					}
				});
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
			//for (int y = 0; y < this.Height; ++y)
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
			//}
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

		public static VoxelMap CreateSlopeTest1()
		{
			var m = new VoxelMap(new IntSize3(16, 16, 16), Voxel.Empty);

			m.Grid[8, 8, 8].Type = VoxelType.Rock;
			m.Grid[8, 7, 7].Type = VoxelType.Slope;
			m.Grid[8, 7, 8].Type = VoxelType.Slope;
			m.Grid[8, 7, 9].Type = VoxelType.Slope;
			m.Grid[8, 9, 7].Type = VoxelType.Slope;
			m.Grid[8, 9, 8].Type = VoxelType.Slope;
			m.Grid[8, 9, 9].Type = VoxelType.Slope;
			m.Grid[8, 8, 7].Type = VoxelType.Slope;
			m.Grid[8, 8, 9].Type = VoxelType.Slope;

			return m;
		}

		public static VoxelMap CreateSlopeTest2()
		{
			var m = new VoxelMap(new IntSize3(16, 16, 16), Voxel.Empty);

			m.Grid[8, 8, 8].Type = VoxelType.Rock;
			m.Grid[8, 8, 7].Type = VoxelType.Rock;
			m.Grid[8, 8, 9].Type = VoxelType.Rock;
			m.Grid[8, 7, 8].Type = VoxelType.Rock;
			m.Grid[8, 9, 8].Type = VoxelType.Rock;

			m.Grid[8, 7, 7].Type = VoxelType.Slope;
			m.Grid[8, 7, 9].Type = VoxelType.Slope;
			m.Grid[8, 9, 7].Type = VoxelType.Slope;
			m.Grid[8, 9, 9].Type = VoxelType.Slope;

			m.Grid[8, 10, 7].Type = VoxelType.Slope;
			m.Grid[8, 10, 8].Type = VoxelType.Slope;
			m.Grid[8, 10, 9].Type = VoxelType.Slope;

			return m;
		}

		public static VoxelMap CreateSlopeTest3()
		{
			var m = new VoxelMap(new IntSize3(16, 16, 16), Voxel.Empty);

			for (int type = 0; type < 5; ++type)
			{
				for (int i = 0; i < 4; ++i)
				{
					m.Grid[2 + type * 2, 8, 4 + i * 2] = new Voxel()
					{
						Type = VoxelType.Slope,
						SlopeType = (byte)type,
						Dir = (byte)i,
						VisibleFaces = FaceDirectionBits.All,
					};
				}
			}

			return m;
		}

		public static VoxelMap CreateFromTileData(TileData[, ,] tileData)
		{
			int d = tileData.GetLength(0);
			int h = tileData.GetLength(1);
			int w = tileData.GetLength(2);

			var size = new IntSize3(w, h, d);

			var map = new VoxelMap(size);

			for (int z = 0; z < d; ++z)
				for (int y = 0; y < h; ++y)
					for (int x = 0; x < w; ++x)
					{
						ConvertTile(x, y, z, map.Grid, tileData);
					}

			return map;
		}

		static void ConvertTile(int x, int y, int z, Voxel[, ,] grid, TileData[, ,] tileData)
		{
			var td = tileData[z, y, x];

			if (td.IsUndefined)
			{
				grid[z, y, x].Type = VoxelType.Undefined;
				return;
			}

			if (td.IsEmpty)
			{
				grid[z, y, x].Type = VoxelType.Empty;
				return;
			}

			if (td.InteriorID == InteriorID.NaturalWall)
			{
				grid[z, y, x].Type = VoxelType.Rock;
				return;
			}

			if (td.WaterLevel > 0)
			{
				grid[z, y, x].Type = VoxelType.Water;
				return;
			}

			if (td.TerrainID.IsSlope())
			{
				grid[z, y, x].Type = VoxelType.Slope;
				if (td.IsGreen)
					grid[z, y, x].Flags |= VoxelFlags.Grass;
				return;
			}

			if (td.IsGreen)
			{
				grid[z - 1, y, x].Flags |= VoxelFlags.Grass;

				Dwarrowdelf.MWCRandom r = new MWCRandom(new IntVector3(x, y, z), 0);

				if (r.Next(100) < 30)
				{
					grid[z, y, x].Flags |= VoxelFlags.Tree;
					grid[z - 1, y, x].Flags |= VoxelFlags.Tree2;
				}
			}

			grid[z, y, x].Type = VoxelType.Empty;
		}

		public static VoxelMap Load(string path)
		{
			using (var stream = File.OpenRead(path))
			using (var br = new BinaryReader(stream))
			{
				int w = br.ReadInt32();
				int h = br.ReadInt32();
				int d = br.ReadInt32();

				var size = new IntSize3(w, h, d);

				var map = new VoxelMap(size);

				var grid = map.Grid;

				for (int z = 0; z < d; ++z)
					for (int y = 0; y < h; ++y)
						for (int x = 0; x < w; ++x)
						{
							grid[z, y, x].Raw = br.ReadUInt64();
						}

				return map;
			}
		}

		public void Save(string path)
		{
			using (var stream = File.Create(path))
			using (var bw = new BinaryWriter(stream))
			{
				bw.Write(this.Size.Width);
				bw.Write(this.Size.Height);
				bw.Write(this.Size.Depth);

				int w = this.Width;
				int h = this.Height;
				int d = this.Depth;

				for (int z = 0; z < d; ++z)
					for (int y = 0; y < h; ++y)
						for (int x = 0; x < w; ++x)
							bw.Write(this.Grid[z, y, x].Raw);
			}
		}
	}
}
