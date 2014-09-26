using Dwarrowdelf;
/**
 * This is a Java greedy meshing implementation based on the javascript implementation 
 * written by Mikola Lysenko and described in this blog post:
 * 
 * http://0fps.wordpress.com/2012/06/30/meshing-in-a-minecraft-game/
 * 
 * The principal changes are:
 * 
 *  - Porting to Java
 *  - Modification in order to compare *voxel faces*, rather than voxels themselves
 *  - Modification to provide for comparison based on multiple attributes simultaneously
 * 
 * This class is ready to be used on the JMonkey platform - but the algorithm should be 
 * usable in any case.
 * 
 * @author Rob O'Leary
 */
using SharpDX;
using System;
using System.Diagnostics;

namespace Client3D
{
	class Greedy
	{
		Chunk m_chunk;

		public Greedy(Chunk chunk)
		{
			m_chunk = chunk;
		}

		/*
		 * These are just constants to keep track of which face we're dealing with - their actual 
		 * values are unimportantly - only that they're constant.
		 */
		private const int SOUTH = 2;
		private const int NORTH = 3;
		private const int EAST = 0;
		private const int WEST = 1;
		private const int TOP = 4;
		private const int BOTTOM = 5;

		/**
		 * This class is used to encapsulate all information about a single voxel face.  Any number of attributes can be 
		 * included - and the equals function will be called in order to compare faces.  This is important because it 
		 * allows different faces of the same voxel to be merged based on varying attributes.
		 * 
		 * Each face can contain vertex data - for example, int[] sunlight, in order to compare vertex attributes.
		 * 
		 * Since it's optimal to combine greedy meshing with face culling, I have included a "transparent" attribute here 
		 * and the mesher skips transparent voxel faces.  The getVoxelData function below - or whatever it's equivalent 
		 * might be when this algorithm is used in a real engine - could set the transparent attribute on faces based 
		 * on whether they should be visible or not.
		 */
		public class VoxelFace
		{
			public bool Transparent;
			public VoxelType Type;
			public VoxelFlags Flags;
			public Byte4 Occ;

			public FaceDirection Side;

			public bool Equals(VoxelFace face)
			{
				return face.Transparent == this.Transparent && face.Type == this.Type && face.Flags == this.Flags &&
					face.Occ.X == this.Occ.X &&
					face.Occ.Y == this.Occ.Y &&
					face.Occ.Z == this.Occ.Z &&
					face.Occ.W == this.Occ.W
					;
			}
		}

		public void Run(Quad quad, FaceDirectionBits globalFaceMask)
		{
			int[] x = new int[] { 0, 0, 0 };
			int[] q = new int[] { 0, 0, 0 };
			int[] du = new int[] { 0, 0, 0 };
			int[] dv = new int[] { 0, 0, 0 };

			/*
			 * We create a mask - this will contain the groups of matching voxel faces 
			 * as we proceed through the chunk in 6 directions - once for each face.
			 */
			VoxelFace[] mask = new VoxelFace[Chunk.CHUNK_SIZE * Chunk.CHUNK_SIZE];

			/**
			 * We start with the lesser-spotted bool for-loop (also known as the old flippy floppy). 
			 * 
			 * The variable backFace will be TRUE on the first iteration and FALSE on the second - this allows 
			 * us to track which direction the indices should run during creation of the quad.
			 * 
			 * This loop runs twice, and the inner loop 3 times - totally 6 iterations - one for each 
			 * voxel face.
			 */
			for (bool backFace = true, b = false; b != backFace; backFace = backFace && b, b = !b)
			{
				/*
				 * We sweep over the 3 dimensions - most of what follows is well described by Mikola Lysenko 
				 * in his post - and is ported from his Javascript implementation.  Where this implementation 
				 * diverges, I've added commentary.
				 */
				for (int d = 0; d < 3; d++)
				{
					int u = (d + 1) % 3;
					int v = (d + 2) % 3;

					x[0] = 0;
					x[1] = 0;
					x[2] = 0;

					q[0] = 0;
					q[1] = 0;
					q[2] = 0;
					q[d] = 1;

					/*
					 * Here we're keeping track of the side that we're meshing.
					 */
					FaceDirection side;

					if (d == 0)
						side = backFace ? FaceDirection.NegativeX : FaceDirection.PositiveX;
					else if (d == 1)
						side = backFace ? FaceDirection.NegativeY : FaceDirection.PositiveY;
					else if (d == 2)
						side = backFace ? FaceDirection.NegativeZ : FaceDirection.PositiveZ;
					else
						throw new Exception();

					if (((1 << (int)side) & (int)globalFaceMask) == 0)
						continue;

					/*
					 * We move through the dimension from front to back
					 */
					for (x[d] = -1; x[d] < Chunk.CHUNK_SIZE; )
					{
						/*
						 * We compute the mask
						 */
						int n = 0;

						for (x[v] = 0; x[v] < Chunk.CHUNK_SIZE; x[v]++)
						{
							for (x[u] = 0; x[u] < Chunk.CHUNK_SIZE; x[u]++)
							{
								/*
								 * Here we retrieve two voxel faces for comparison.
								 */
								VoxelFace voxelFace = (x[d] >= 0) ? GetVoxelFace(x[0], x[1], x[2], side) : null;
								VoxelFace voxelFace1 = (x[d] < Chunk.CHUNK_SIZE - 1) ? GetVoxelFace(x[0] + q[0], x[1] + q[1], x[2] + q[2], side) : null;

								/*
								 * Note that we're using the equals function in the voxel face class here, which lets the faces 
								 * be compared based on any number of attributes.
								 * 
								 * Also, we choose the face to add to the mask depending on whether we're moving through on a backface or not.
								 */
								mask[n++] = ((voxelFace != null && voxelFace1 != null && voxelFace.Equals(voxelFace1)))
											? null
											: backFace ? voxelFace1 : voxelFace;
							}
						}

						x[d]++;

						/*
						 * Now we generate the mesh for the mask
						 */
						n = 0;

						for (int j = 0; j < Chunk.CHUNK_SIZE; j++)
						{
							for (int i = 0; i < Chunk.CHUNK_SIZE; )
							{
								if (mask[n] == null)
								{
									i++;
									n++;
								}
								else
								{
									int w, h;

									/*
									 * We compute the width
									 */
									for (w = 1; i + w < Chunk.CHUNK_SIZE && mask[n + w] != null && mask[n + w].Equals(mask[n]); w++)
									{
									}

									/*
									 * Then we compute height
									 */
									bool done = false;

									for (h = 1; j + h < Chunk.CHUNK_SIZE; h++)
									{
										for (int k = 0; k < w; k++)
										{
											if (mask[n + k + h * Chunk.CHUNK_SIZE] == null || !mask[n + k + h * Chunk.CHUNK_SIZE].Equals(mask[n]))
											{
												done = true;
												break;
											}
										}

										if (done)
											break;
									}

									/*
									 * Here we check the "transparent" attribute in the VoxelFace class to ensure that we don't mesh 
									 * any culled faces.
									 */
									if (!mask[n].Transparent)
									{
										/*
										 * Add quad
										 */
										x[u] = i;
										x[v] = j;

										du[0] = 0;
										du[1] = 0;
										du[2] = 0;
										du[u] = w;

										dv[0] = 0;
										dv[1] = 0;
										dv[2] = 0;
										dv[v] = h;

										/*
										 * And here we call the quad function in order to render a merged quad in the scene.
										 * 
										 * We pass mask[n] to the function, which is an instance of the VoxelFace class containing 
										 * all the attributes of the face - which allows for variables to be passed to shaders - for 
										 * example lighting values used to create ambient occlusion.
										 */
										quad(new Vector3(x[0], x[1], x[2]),
											 new Vector3(x[0] + du[0], x[1] + du[1], x[2] + du[2]),
											 new Vector3(x[0] + du[0] + dv[0], x[1] + du[1] + dv[1], x[2] + du[2] + dv[2]),
											 new Vector3(x[0] + dv[0], x[1] + dv[1], x[2] + dv[2]),
											 w,
											 h,
											 mask[n],
											 backFace);
									}

									/*
									 * We zero out the mask
									 */
									for (int l = 0; l < h; ++l)
										for (int k = 0; k < w; ++k)
											mask[n + k + l * Chunk.CHUNK_SIZE] = null;

									/*
									 * And then finally increment the counters and continue
									 */
									i += w;
									n += w;
								}
							}
						}
					}
				}
			}
		}

		/**
		 * This function returns an instance of VoxelFace containing the attributes for 
		 * one side of a voxel.  In this simple demo we just return a value from the 
		 * sample data array.  However, in an actual voxel engine, this function would 
		 * check if the voxel face should be culled, and set per-face and per-vertex 
		 * values as well as voxel values in the returned instance.
		 * 
		 * @param x
		 * @param y
		 * @param z
		 * @param face
		 * @return 
		 */
		VoxelFace GetVoxelFace(int x, int y, int z, FaceDirection side)
		{
			x += m_chunk.ChunkOffset.X;
			y += m_chunk.ChunkOffset.Y;
			z += m_chunk.ChunkOffset.Z;

			var vd = GlobalData.VoxelMap.Grid[z, y, x];

			bool transp = ((int)vd.VisibleFaces & (1 << (int)side)) == 0;

			int i = -1;

			switch (side)
			{
				case FaceDirection.PositiveX:
					i = 2; break;
				case FaceDirection.PositiveY:
					i = 3; break;
				case FaceDirection.PositiveZ:
					i = 0; break;

				case FaceDirection.NegativeX:
					i = 0; break;
				case FaceDirection.NegativeY:
					i = 3; break;
				case FaceDirection.NegativeZ:
					i = 0; break;

				default:
					throw new Exception();
			}

			int occ0, occ1, occ2, occ3;

			// XXX doesn't work.
			Chunk.GetOcclusionsForFace(new IntVector3(x, y, z), side,
				out occ0, out occ1, out occ2, out occ3);

			//int occ0 = Chunk.GetOcclusionForFaceVertex(new IntVector3(x, y, z), side, (i + 0) % 4);
			//int occ1 = Chunk.GetOcclusionForFaceVertex(new IntVector3(x, y, z), side, (i + 1) % 4);
			//int occ2 = Chunk.GetOcclusionForFaceVertex(new IntVector3(x, y, z), side, (i + 3) % 4);
			//int occ3 = Chunk.GetOcclusionForFaceVertex(new IntVector3(x, y, z), side, (i + 2) % 4);

			VoxelFace voxelFace = new VoxelFace()
			{
				Type = vd.Type,
				Transparent = vd.Type == VoxelType.Empty || transp,
				Flags = vd.Flags,
				Occ = new Byte4(occ0, occ1, occ2, occ3),
			};

			voxelFace.Side = side;

			return voxelFace;
		}

		public delegate void Quad(Vector3 bottomLeft,
				  Vector3 topLeft,
				  Vector3 topRight,
				  Vector3 bottomRight,
				  int width,
				  int height,
				  VoxelFace voxel,
				  bool backFace);
	}
}