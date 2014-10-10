using Dwarrowdelf;
using System;
using System.Collections.Generic;
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
		public Direction VisibleFaces;
		[FieldOffset(2)]
		public VoxelFlags Flags;
		[FieldOffset(3)]
		public byte Dir;
		[FieldOffset(4)]
		public byte SlopeType;

		/// <summary>
		/// The voxel can be seen through, but may contain something to draw
		/// </summary>
		public bool IsTransparent { get { return this.Type == VoxelType.Empty; } }

		/// <summary>
		/// The voxel cannot be seen through
		/// </summary>
		public bool IsOpaque
		{
			get
			{
				switch (this.Type)
				{
					case VoxelType.Rock:
					case VoxelType.Undefined:
					case VoxelType.Water:
						return true;
					default:
						return false;
				}
			}
		}

		public bool IsEmpty { get { return this.Type == VoxelType.Empty && this.Flags == 0; } }

		public readonly static Voxel Empty = new Voxel() { Type = VoxelType.Empty };
		public readonly static Voxel Rock = new Voxel() { Type = VoxelType.Rock };
		public readonly static Voxel Water = new Voxel() { Type = VoxelType.Water };
		public readonly static Voxel Slope = new Voxel() { Type = VoxelType.Slope };

		public override string ToString()
		{
			return string.Format("{0}, [{1}], [{2}]", this.Type, this.VisibleFaces, this.Flags);
		}
	}
}
